#include "BppReplayMediaFoundation.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3d10.h>
#include <dxgi.h>
#include <evr.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfobjects.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

using Microsoft::WRL::ComPtr;

#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)

struct UnityInterfaceGUID
{
    unsigned long long high;
    unsigned long long low;
};

struct IUnityInterface
{
};

struct IUnityInterfaces
{
    IUnityInterface *(UNITY_INTERFACE_API *GetInterface)(UnityInterfaceGUID guid);
    void(UNITY_INTERFACE_API *RegisterInterface)(UnityInterfaceGUID guid, IUnityInterface *value);
    IUnityInterface *(UNITY_INTERFACE_API *GetInterfaceSplit)(
        unsigned long long guidHigh,
        unsigned long long guidLow);
    void(UNITY_INTERFACE_API *RegisterInterfaceSplit)(
        unsigned long long guidHigh,
        unsigned long long guidLow,
        IUnityInterface *value);
};

struct RenderSurfaceBase;
using UnityRenderBuffer = RenderSurfaceBase *;
using UnityTextureID = unsigned int;

struct IUnityGraphicsD3D11 : IUnityInterface
{
    ID3D11Device *(UNITY_INTERFACE_API *GetDevice)();
    ID3D11Resource *(UNITY_INTERFACE_API *TextureFromRenderBuffer)(UnityRenderBuffer buffer);
    ID3D11Resource *(UNITY_INTERFACE_API *TextureFromNativeTexture)(UnityTextureID texture);
    ID3D11RenderTargetView *(UNITY_INTERFACE_API *RTVFromRenderBuffer)(UnityRenderBuffer surface);
    ID3D11ShaderResourceView *(UNITY_INTERFACE_API *SRVFromNativeTexture)(UnityTextureID texture);
};

namespace
{
constexpr int kSlotCount = 8;
constexpr LONGLONG kTicksPerSecond = 10'000'000;
constexpr unsigned long long kD3D11GuidHigh = 0xAAB37EF87A87D748ULL;
constexpr unsigned long long kD3D11GuidLow = 0xBF76967F07EFB177ULL;

IUnityGraphicsD3D11 *g_unityD3D11 = nullptr;
std::mutex g_mfMutex;
int g_mfReferences = 0;

enum class SlotState : uint8_t
{
    Free,
    GpuPending,
    Encoding,
};

struct Encoder;

struct FrameSlot
{
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11VideoProcessorOutputView> outputView;
    SlotState state = SlotState::Free;
};

struct Encoder
{
    std::mutex mutex;
    std::condition_variable condition;
    std::string error;
    std::string encoderName;
    bool failed = false;
    bool acceptingFrames = true;
    bool finished = false;
    bool mfStarted = false;
    bool destroyRequested = false;
    bool destroyScheduled = false;
    BOOL previousMultithreadProtection = FALSE;
    int fps = 60;
    int inFlight = 0;
    int renderEventsPending = 0;
    BppMfNativeStats stats{};

    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D10Multithread> multithread;
    ComPtr<ID3D11VideoDevice> videoDevice;
    ComPtr<ID3D11VideoContext> videoContext;
    ComPtr<ID3D11VideoProcessorEnumerator> videoEnumerator;
    ComPtr<ID3D11VideoProcessor> videoProcessor;
    ComPtr<ID3D11Texture2D> sourceTexture;
    ComPtr<ID3D11Texture2D> processorSourceTexture;
    ComPtr<ID3D11VideoProcessorInputView> sourceView;
    std::vector<FrameSlot> slots;

    UINT resetToken = 0;
    DWORD streamIndex = 0;
    ComPtr<IMFDXGIDeviceManager> deviceManager;
    ComPtr<IMFSinkWriter> sinkWriter;
};

struct RenderEventPacket
{
    Encoder *encoder;
    int slotIndex;
    int64_t firstFrameIndex;
    int frameCount;
    std::atomic<uint8_t> state{0};
    std::atomic<int> references{2};
};

void MaybeScheduleDestroy(Encoder *encoder);

class SlotReleaseCallback final : public IMFAsyncCallback
{
  public:
    SlotReleaseCallback(Encoder *encoder, int slotIndex, int count)
        : encoder_(encoder), slotIndex_(slotIndex), remaining_(count)
    {
    }

    STDMETHODIMP QueryInterface(REFIID iid, void **value) override
    {
        if (value == nullptr)
            return E_POINTER;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IMFAsyncCallback))
        {
            *value = static_cast<IMFAsyncCallback *>(this);
            AddRef();
            return S_OK;
        }
        *value = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override
    {
        return static_cast<ULONG>(references_.fetch_add(1) + 1);
    }

    STDMETHODIMP_(ULONG) Release() override
    {
        const ULONG remaining = static_cast<ULONG>(references_.fetch_sub(1) - 1);
        if (remaining == 0)
            delete this;
        return remaining;
    }

    STDMETHODIMP GetParameters(DWORD *, DWORD *) override
    {
        return E_NOTIMPL;
    }

    STDMETHODIMP Invoke(IMFAsyncResult *) override
    {
        if (remaining_.fetch_sub(1) != 1)
            return S_OK;

        {
            std::lock_guard<std::mutex> guard(encoder_->mutex);
            if (slotIndex_ >= 0 && slotIndex_ < static_cast<int>(encoder_->slots.size()))
                encoder_->slots[static_cast<size_t>(slotIndex_)].state = SlotState::Free;
            encoder_->inFlight = std::max(0, encoder_->inFlight - 1);
            encoder_->condition.notify_all();
        }
        MaybeScheduleDestroy(encoder_);
        return S_OK;
    }

  private:
    ~SlotReleaseCallback() = default;
    std::atomic<ULONG> references_{1};
    Encoder *encoder_;
    int slotIndex_;
    std::atomic<int> remaining_;
};

std::string HResultMessage(const char *operation, HRESULT result)
{
    char text[256];
    std::snprintf(
        text,
        sizeof(text),
        "%s failed with HRESULT 0x%08lX.",
        operation,
        static_cast<unsigned long>(result));
    return text;
}

std::wstring Utf8ToWide(const char *value)
{
    if (value == nullptr || value[0] == '\0')
        return {};
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
    if (length <= 1)
        return {};
    std::wstring result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, result.data(), length);
    result.pop_back();
    return result;
}

std::string WideToUtf8(const wchar_t *value)
{
    if (value == nullptr || value[0] == L'\0')
        return {};
    const int length = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1)
        return {};
    std::string result(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), length, nullptr, nullptr);
    result.pop_back();
    return result;
}

void SetFailure(Encoder *encoder, const std::string &message)
{
    if (encoder == nullptr)
        return;
    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (encoder->error.empty())
        encoder->error = message;
    encoder->failed = true;
    encoder->acceptingFrames = false;
    encoder->condition.notify_all();
}

bool StartMediaFoundation(std::string &error)
{
    std::lock_guard<std::mutex> guard(g_mfMutex);
    if (g_mfReferences == 0)
    {
        const HRESULT result = MFStartup(MF_VERSION, MFSTARTUP_FULL);
        if (FAILED(result))
        {
            error = HResultMessage("MFStartup", result);
            return false;
        }
    }
    ++g_mfReferences;
    return true;
}

void StopMediaFoundation()
{
    std::lock_guard<std::mutex> guard(g_mfMutex);
    if (g_mfReferences <= 0)
        return;
    --g_mfReferences;
    if (g_mfReferences == 0)
        MFShutdown();
}

DWORD WINAPI DestroyEncoderWorker(void *context)
{
    auto *encoder = static_cast<Encoder *>(context);
    if (encoder->multithread != nullptr)
        encoder->multithread->SetMultithreadProtected(
            encoder->previousMultithreadProtection);
    encoder->sinkWriter.Reset();
    const bool stopMediaFoundation = encoder->mfStarted;
    encoder->mfStarted = false;
    delete encoder;
    if (stopMediaFoundation)
        StopMediaFoundation();
    return 0;
}

void MaybeScheduleDestroy(Encoder *encoder)
{
    bool schedule = false;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        if (encoder->destroyRequested && !encoder->destroyScheduled &&
            encoder->renderEventsPending == 0 && encoder->inFlight == 0)
        {
            encoder->destroyScheduled = true;
            schedule = true;
        }
    }
    if (!schedule)
        return;
    if (!QueueUserWorkItem(&DestroyEncoderWorker, encoder, WT_EXECUTEDEFAULT))
        DestroyEncoderWorker(encoder);
}

bool SetMediaTypeSize(IMFMediaType *type, REFGUID key, UINT32 width, UINT32 height)
{
    return SUCCEEDED(MFSetAttributeSize(type, key, width, height));
}

bool SetMediaTypeRatio(IMFMediaType *type, REFGUID key, UINT32 numerator, UINT32 denominator)
{
    return SUCCEEDED(MFSetAttributeRatio(type, key, numerator, denominator));
}

std::unordered_map<std::string, std::string> EnumerateHardwareEncoders()
{
    std::unordered_map<std::string, std::string> encoders;
    MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, MFVideoFormat_NV12};
    MFT_REGISTER_TYPE_INFO output{MFMediaType_Video, MFVideoFormat_H264};
    IMFActivate **activations = nullptr;
    UINT32 count = 0;
    const HRESULT result = MFTEnumEx(
        MFT_CATEGORY_VIDEO_ENCODER,
        MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
        &input,
        &output,
        &activations,
        &count);
    if (FAILED(result))
        return encoders;

    for (UINT32 index = 0; index < count; ++index)
    {
        GUID clsid{};
        wchar_t *name = nullptr;
        UINT32 nameLength = 0;
        if (SUCCEEDED(activations[index]->GetGUID(MFT_TRANSFORM_CLSID_Attribute, &clsid)))
        {
            wchar_t clsidText[64]{};
            StringFromGUID2(clsid, clsidText, static_cast<int>(std::size(clsidText)));
            if (SUCCEEDED(activations[index]->GetAllocatedString(
                    MFT_FRIENDLY_NAME_Attribute,
                    &name,
                    &nameLength)))
            {
                encoders.emplace(WideToUtf8(clsidText), WideToUtf8(name));
                CoTaskMemFree(name);
            }
            else
            {
                encoders.emplace(WideToUtf8(clsidText), WideToUtf8(clsidText));
            }
        }
        activations[index]->Release();
    }
    CoTaskMemFree(activations);
    return encoders;
}

bool TransformOutputsH264(IMFTransform *transform)
{
    DWORD inputCount = 0;
    DWORD outputCount = 0;
    if (FAILED(transform->GetStreamCount(&inputCount, &outputCount)) || outputCount == 0)
        return false;

    std::vector<DWORD> inputIds(inputCount);
    std::vector<DWORD> outputIds(outputCount);
    const HRESULT idsResult = transform->GetStreamIDs(
        inputCount,
        inputIds.data(),
        outputCount,
        outputIds.data());
    if (idsResult == E_NOTIMPL)
    {
        for (DWORD index = 0; index < outputCount; ++index)
            outputIds[index] = index;
    }
    else if (FAILED(idsResult))
    {
        return false;
    }

    for (const DWORD outputId : outputIds)
    {
        ComPtr<IMFMediaType> type;
        if (SUCCEEDED(transform->GetOutputCurrentType(outputId, &type)))
        {
            GUID major{};
            GUID subtype{};
            if (SUCCEEDED(type->GetGUID(MF_MT_MAJOR_TYPE, &major)) &&
                SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)) &&
                major == MFMediaType_Video && subtype == MFVideoFormat_H264)
                return true;
        }
        for (DWORD typeIndex = 0;; ++typeIndex)
        {
            type.Reset();
            if (FAILED(transform->GetOutputAvailableType(outputId, typeIndex, &type)))
                break;
            GUID major{};
            GUID subtype{};
            if (SUCCEEDED(type->GetGUID(MF_MT_MAJOR_TYPE, &major)) &&
                SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)) &&
                major == MFMediaType_Video && subtype == MFVideoFormat_H264)
                return true;
        }
    }
    return false;
}

bool VerifyHardwareEncoder(Encoder *encoder)
{
    const auto hardware = EnumerateHardwareEncoders();
    if (hardware.empty())
    {
        SetFailure(encoder, "Media Foundation did not enumerate a hardware NV12 to H.264 encoder.");
        return false;
    }

    ComPtr<IMFSinkWriterEx> writerEx;
    HRESULT result = encoder->sinkWriter.As(&writerEx);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("IMFSinkWriterEx", result));
        return false;
    }

    for (DWORD transformIndex = 0;; ++transformIndex)
    {
        GUID category{};
        ComPtr<IMFTransform> transform;
        result = writerEx->GetTransformForStream(
            encoder->streamIndex,
            transformIndex,
            &category,
            &transform);
        if (FAILED(result))
            break;

        if (category != MFT_CATEGORY_VIDEO_ENCODER)
            continue;

        ComPtr<IMFAttributes> attributes;
        GUID clsid{};
        if (FAILED(transform->GetAttributes(&attributes)))
            continue;

        if (SUCCEEDED(attributes->GetGUID(MFT_TRANSFORM_CLSID_Attribute, &clsid)))
        {
            wchar_t clsidText[64]{};
            StringFromGUID2(clsid, clsidText, static_cast<int>(std::size(clsidText)));
            const auto found = hardware.find(WideToUtf8(clsidText));
            if (found != hardware.end())
            {
                encoder->encoderName = found->second;
                return true;
            }
        }

        if (!TransformOutputsH264(transform.Get()))
            continue;

        wchar_t *hardwareUrl = nullptr;
        UINT32 hardwareUrlLength = 0;
        if (SUCCEEDED(attributes->GetAllocatedString(
                MFT_ENUM_HARDWARE_URL_Attribute,
                &hardwareUrl,
                &hardwareUrlLength)) &&
            hardwareUrl != nullptr && hardwareUrlLength > 0)
        {
            encoder->encoderName = WideToUtf8(hardwareUrl);
            CoTaskMemFree(hardwareUrl);
            return true;
        }
    }

    SetFailure(
        encoder,
        "Sink Writer selected a transform that is not in Media Foundation's hardware H.264 catalog.");
    return false;
}

bool InitializeVideoProcessor(Encoder *encoder, int width, int height)
{
    D3D11_TEXTURE2D_DESC sourceDescription{};
    encoder->sourceTexture->GetDesc(&sourceDescription);
    if (sourceDescription.SampleDesc.Count != 1)
    {
        SetFailure(encoder, "The persistent Unity capture texture must not be multisampled.");
        return false;
    }
    // Recording must hand the encoder Unity's final gamma-encoded bytes unchanged. A TYPELESS
    // resource cannot be bound as a view at all, and an _SRGB view would be read with
    // sRGB-to-linear semantics, so both are mapped onto the plain UNORM twin. When that changes
    // the format, the block below allocates a typed texture of that format and each frame is
    // bit-copied into it — the same bits reinterpreted, never a color conversion.
    DXGI_FORMAT processorSourceFormat = sourceDescription.Format;
    switch (sourceDescription.Format)
    {
    case DXGI_FORMAT_B8G8R8A8_TYPELESS:
    case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
        processorSourceFormat = DXGI_FORMAT_B8G8R8A8_UNORM;
        break;
    case DXGI_FORMAT_B8G8R8A8_UNORM:
        break;
    case DXGI_FORMAT_R8G8B8A8_TYPELESS:
    case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
        processorSourceFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        break;
    case DXGI_FORMAT_R8G8B8A8_UNORM:
        break;
    default:
    {
        char message[160];
        std::snprintf(
            message,
            sizeof(message),
            "Unsupported Unity D3D11 capture texture DXGI format %u.",
            static_cast<unsigned>(sourceDescription.Format));
        SetFailure(encoder, message);
        return false;
    }
    }

    if (processorSourceFormat != sourceDescription.Format)
    {
        D3D11_TEXTURE2D_DESC processorSourceDescription = sourceDescription;
        processorSourceDescription.MipLevels = 1;
        processorSourceDescription.ArraySize = 1;
        processorSourceDescription.Format = processorSourceFormat;
        processorSourceDescription.Usage = D3D11_USAGE_DEFAULT;
        processorSourceDescription.CPUAccessFlags = 0;
        processorSourceDescription.MiscFlags = 0;
        HRESULT result = encoder->device->CreateTexture2D(
            &processorSourceDescription,
            nullptr,
            &encoder->processorSourceTexture);
        if (FAILED(result))
        {
            SetFailure(encoder, HResultMessage("CreateTexture2D(typed RGB capture alias)", result));
            return false;
        }
    }
    else
    {
        encoder->processorSourceTexture = encoder->sourceTexture;
    }

    HRESULT result = encoder->device.As(&encoder->videoDevice);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("ID3D11VideoDevice", result));
        return false;
    }
    result = encoder->context.As(&encoder->videoContext);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("ID3D11VideoContext", result));
        return false;
    }

    D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
    content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    content.InputFrameRate = {static_cast<UINT>(encoder->fps), 1};
    content.InputWidth = sourceDescription.Width;
    content.InputHeight = sourceDescription.Height;
    content.OutputFrameRate = {static_cast<UINT>(encoder->fps), 1};
    content.OutputWidth = static_cast<UINT>(width);
    content.OutputHeight = static_cast<UINT>(height);
    content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
    result = encoder->videoDevice->CreateVideoProcessorEnumerator(
        &content,
        &encoder->videoEnumerator);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("CreateVideoProcessorEnumerator", result));
        return false;
    }
    result = encoder->videoDevice->CreateVideoProcessor(
        encoder->videoEnumerator.Get(),
        0,
        &encoder->videoProcessor);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("CreateVideoProcessor", result));
        return false;
    }

    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription{};
    inputDescription.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    inputDescription.Texture2D.MipSlice = 0;
    inputDescription.Texture2D.ArraySlice = 0;
    result = encoder->videoDevice->CreateVideoProcessorInputView(
        encoder->processorSourceTexture.Get(),
        encoder->videoEnumerator.Get(),
        &inputDescription,
        &encoder->sourceView);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("CreateVideoProcessorInputView", result));
        return false;
    }

    D3D11_TEXTURE2D_DESC slotDescription{};
    slotDescription.Width = static_cast<UINT>(width);
    slotDescription.Height = static_cast<UINT>(height);
    slotDescription.MipLevels = 1;
    slotDescription.ArraySize = 1;
    slotDescription.Format = DXGI_FORMAT_NV12;
    slotDescription.SampleDesc.Count = 1;
    slotDescription.Usage = D3D11_USAGE_DEFAULT;
    slotDescription.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

    encoder->slots.resize(kSlotCount);
    for (auto &slot : encoder->slots)
    {
        result = encoder->device->CreateTexture2D(&slotDescription, nullptr, &slot.texture);
        if (FAILED(result))
        {
            SetFailure(encoder, HResultMessage("CreateTexture2D(NV12)", result));
            return false;
        }
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputDescription{};
        outputDescription.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
        outputDescription.Texture2D.MipSlice = 0;
        result = encoder->videoDevice->CreateVideoProcessorOutputView(
            slot.texture.Get(),
            encoder->videoEnumerator.Get(),
            &outputDescription,
            &slot.outputView);
        if (FAILED(result))
        {
            SetFailure(encoder, HResultMessage("CreateVideoProcessorOutputView", result));
            return false;
        }
    }

    const RECT sourceRect{0, 0, static_cast<LONG>(sourceDescription.Width), static_cast<LONG>(sourceDescription.Height)};
    const RECT targetRect{0, 0, width, height};
    encoder->videoContext->VideoProcessorSetStreamSourceRect(
        encoder->videoProcessor.Get(), 0, TRUE, &sourceRect);
    encoder->videoContext->VideoProcessorSetStreamDestRect(
        encoder->videoProcessor.Get(), 0, TRUE, &targetRect);
    encoder->videoContext->VideoProcessorSetOutputTargetRect(
        encoder->videoProcessor.Get(), TRUE, &targetRect);
    // The two ends deliberately use different range fields. The input is RGB, whose range lives
    // in RGB_Range (0 = full 0-255). The NV12 output is YCbCr, whose range must come from
    // Nominal_Range: putting it in the RGB field on the output side leaves the YCbCr range
    // unstated and lets the driver default it, which need not agree with the
    // MFNominalRange_16_235 / BT.709 media type this encoder declares — that mismatch is what
    // produced black-level and contrast drift on some drivers. YCbCr_Matrix = 1 is BT.709 on
    // both ends.
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE inputColor{};
    inputColor.RGB_Range = 0;
    inputColor.YCbCr_Matrix = 1;
    encoder->videoContext->VideoProcessorSetStreamColorSpace(
        encoder->videoProcessor.Get(), 0, &inputColor);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE outputColor{};
    outputColor.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
    outputColor.YCbCr_Matrix = 1;
    encoder->videoContext->VideoProcessorSetOutputColorSpace(
        encoder->videoProcessor.Get(), &outputColor);
    return true;
}

bool InitializeSinkWriter(
    Encoder *encoder,
    const std::wstring &path,
    int width,
    int height,
    int bitrateBitsPerSecond)
{
    HRESULT result = MFCreateDXGIDeviceManager(
        &encoder->resetToken,
        &encoder->deviceManager);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("MFCreateDXGIDeviceManager", result));
        return false;
    }
    result = encoder->deviceManager->ResetDevice(encoder->device.Get(), encoder->resetToken);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("IMFDXGIDeviceManager::ResetDevice", result));
        return false;
    }

    ComPtr<IMFAttributes> attributes;
    result = MFCreateAttributes(&attributes, 4);
    if (SUCCEEDED(result))
        result = attributes->SetUnknown(MF_SINK_WRITER_D3D_MANAGER, encoder->deviceManager.Get());
    if (SUCCEEDED(result))
        result = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    if (SUCCEEDED(result))
        result = attributes->SetUINT32(MF_READWRITE_D3D_OPTIONAL, FALSE);
    if (SUCCEEDED(result))
        result = attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("Media Foundation writer attributes", result));
        return false;
    }

    result = MFCreateSinkWriterFromURL(path.c_str(), nullptr, attributes.Get(), &encoder->sinkWriter);
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("MFCreateSinkWriterFromURL", result));
        return false;
    }

    ComPtr<IMFMediaType> outputType;
    result = MFCreateMediaType(&outputType);
    if (SUCCEEDED(result))
        result = outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(result))
        result = outputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(bitrateBitsPerSecond));
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
    if (SUCCEEDED(result) && !SetMediaTypeSize(outputType.Get(), MF_MT_FRAME_SIZE, width, height))
        result = E_FAIL;
    if (SUCCEEDED(result) && !SetMediaTypeRatio(outputType.Get(), MF_MT_FRAME_RATE, encoder->fps, 1))
        result = E_FAIL;
    if (SUCCEEDED(result) && !SetMediaTypeRatio(outputType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1))
        result = E_FAIL;
    if (SUCCEEDED(result))
        result = outputType->SetUINT32(MF_MT_MPEG2_PROFILE, 100); // H.264 High profile_idc.
    if (SUCCEEDED(result))
        result = encoder->sinkWriter->AddStream(outputType.Get(), &encoder->streamIndex);

    ComPtr<IMFMediaType> inputType;
    if (SUCCEEDED(result))
        result = MFCreateMediaType(&inputType);
    if (SUCCEEDED(result))
        result = inputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(result))
        result = inputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(
            MF_MT_SAMPLE_SIZE,
            static_cast<UINT32>(width * height * 3 / 2));
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, TRUE);
    if (SUCCEEDED(result))
        result = inputType->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);
    if (SUCCEEDED(result) && !SetMediaTypeSize(inputType.Get(), MF_MT_FRAME_SIZE, width, height))
        result = E_FAIL;
    if (SUCCEEDED(result) && !SetMediaTypeRatio(inputType.Get(), MF_MT_FRAME_RATE, encoder->fps, 1))
        result = E_FAIL;
    if (SUCCEEDED(result) && !SetMediaTypeRatio(inputType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1))
        result = E_FAIL;
    if (SUCCEEDED(result))
        result = encoder->sinkWriter->SetInputMediaType(encoder->streamIndex, inputType.Get(), nullptr);
    if (SUCCEEDED(result))
        result = encoder->sinkWriter->BeginWriting();
    if (FAILED(result))
    {
        SetFailure(encoder, HResultMessage("Media Foundation H.264 writer initialization", result));
        return false;
    }
    return VerifyHardwareEncoder(encoder);
}

void CompleteRenderEvent(Encoder *encoder)
{
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->renderEventsPending = std::max(0, encoder->renderEventsPending - 1);
        encoder->condition.notify_all();
    }
    MaybeScheduleDestroy(encoder);
}

void ReleasePacket(RenderEventPacket *packet)
{
    if (packet != nullptr && packet->references.fetch_sub(1) == 1)
        delete packet;
}

void ReleasePendingSlot(Encoder *encoder, int slotIndex)
{
    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (slotIndex >= 0 && slotIndex < static_cast<int>(encoder->slots.size()) &&
        encoder->slots[static_cast<size_t>(slotIndex)].state == SlotState::GpuPending)
        encoder->slots[static_cast<size_t>(slotIndex)].state = SlotState::Free;
    encoder->condition.notify_all();
}

bool SubmitSlot(Encoder *encoder, int slotIndex, int64_t firstFrameIndex, int frameCount)
{
    auto *callback = new SlotReleaseCallback(encoder, slotIndex, frameCount);
    const LONGLONG fps = std::max(1, encoder->fps);

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->slots[static_cast<size_t>(slotIndex)].state = SlotState::Encoding;
        ++encoder->inFlight;
        encoder->stats.maxInFlight = std::max(encoder->stats.maxInFlight, encoder->inFlight);
    }

    for (int index = 0; index < frameCount; ++index)
    {
        bool allocatorSet = false;
        ComPtr<IMFMediaBuffer> buffer;
        ComPtr<IMFSample> sample;
        HRESULT result = MFCreateDXGISurfaceBuffer(
            __uuidof(ID3D11Texture2D),
            encoder->slots[static_cast<size_t>(slotIndex)].texture.Get(),
            0,
            FALSE,
            &buffer);
        if (SUCCEEDED(result))
        {
            D3D11_TEXTURE2D_DESC description{};
            encoder->slots[static_cast<size_t>(slotIndex)].texture->GetDesc(&description);
            result = buffer->SetCurrentLength(description.Width * description.Height * 3 / 2);
        }
        if (SUCCEEDED(result))
            result = MFCreateVideoSampleFromSurface(nullptr, &sample);
        if (SUCCEEDED(result))
            result = sample->AddBuffer(buffer.Get());
        ComPtr<IMFTrackedSample> tracked;
        if (SUCCEEDED(result))
            result = sample.As(&tracked);
        if (SUCCEEDED(result))
            result = tracked->SetAllocator(callback, nullptr);
        if (SUCCEEDED(result))
            allocatorSet = true;
        if (SUCCEEDED(result))
        {
            const LONGLONG frameIndex = firstFrameIndex + index;
            const LONGLONG sampleStart = frameIndex * kTicksPerSecond / fps;
            const LONGLONG sampleEnd = (frameIndex + 1) * kTicksPerSecond / fps;
            result = sample->SetSampleTime(sampleStart);
            if (SUCCEEDED(result))
                result = sample->SetSampleDuration(sampleEnd - sampleStart);
        }
        if (SUCCEEDED(result))
            result = encoder->sinkWriter->WriteSample(encoder->streamIndex, sample.Get());
        if (FAILED(result))
        {
            const int manualReleases = frameCount - index - (allocatorSet ? 1 : 0);
            for (int release = 0; release < manualReleases; ++release)
                callback->Invoke(nullptr);
            callback->Release();
            SetFailure(encoder, HResultMessage("IMFSinkWriter::WriteSample", result));
            return false;
        }

        std::lock_guard<std::mutex> guard(encoder->mutex);
        ++encoder->stats.submittedFrames;
        ++encoder->stats.writtenFrames;
    }
    callback->Release();
    return true;
}

void UNITY_INTERFACE_API RenderEventCallback(int eventId, void *data)
{
    if (eventId != 1 || data == nullptr)
        return;
    auto *packet = static_cast<RenderEventPacket *>(data);
    const uint8_t oldState = packet->state.exchange(1);
    if (oldState != 0)
    {
        ReleasePacket(packet);
        return;
    }

    Encoder *encoder = packet->encoder;
    const int slotIndex = packet->slotIndex;
    const int64_t firstFrameIndex = packet->firstFrameIndex;
    const int frameCount = packet->frameCount;
    bool canSubmit = false;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        canSubmit = !encoder->failed && !encoder->finished &&
            slotIndex >= 0 && slotIndex < static_cast<int>(encoder->slots.size()) &&
            encoder->slots[static_cast<size_t>(slotIndex)].state == SlotState::GpuPending;
    }
    if (!canSubmit)
    {
        ReleasePendingSlot(encoder, slotIndex);
        CompleteRenderEvent(encoder);
        ReleasePacket(packet);
        return;
    }

    D3D11_VIDEO_PROCESSOR_STREAM stream{};
    stream.Enable = TRUE;
    stream.pInputSurface = encoder->sourceView.Get();
    if (encoder->processorSourceTexture.Get() != encoder->sourceTexture.Get())
    {
        encoder->context->CopySubresourceRegion(
            encoder->processorSourceTexture.Get(),
            0,
            0,
            0,
            0,
            encoder->sourceTexture.Get(),
            0,
            nullptr);
    }
    const HRESULT result = encoder->videoContext->VideoProcessorBlt(
        encoder->videoProcessor.Get(),
        encoder->slots[static_cast<size_t>(slotIndex)].outputView.Get(),
        0,
        1,
        &stream);
    if (FAILED(result))
    {
        ReleasePendingSlot(encoder, slotIndex);
        SetFailure(encoder, HResultMessage("ID3D11VideoContext::VideoProcessorBlt", result));
    }
    else if (!SubmitSlot(encoder, slotIndex, firstFrameIndex, frameCount))
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        ++encoder->stats.encodeErrors;
    }

    CompleteRenderEvent(encoder);
    ReleasePacket(packet);
}

int CopyText(const std::string &text, char *buffer, int capacity)
{
    if (buffer == nullptr || capacity <= 0)
        return 0;
    const int copied = std::min(static_cast<int>(text.size()), capacity - 1);
    if (copied > 0)
        std::memcpy(buffer, text.data(), static_cast<size_t>(copied));
    buffer[copied] = '\0';
    return copied;
}
} // namespace

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(
    IUnityInterfaces *interfaces)
{
    g_unityD3D11 = interfaces == nullptr
        ? nullptr
        : static_cast<IUnityGraphicsD3D11 *>(
              interfaces->GetInterfaceSplit(kD3D11GuidHigh, kD3D11GuidLow));
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    g_unityD3D11 = nullptr;
}

extern "C" int __cdecl BppMfHasUnityD3D11Interface()
{
    return g_unityD3D11 != nullptr && g_unityD3D11->GetDevice() != nullptr ? 1 : 0;
}

extern "C" int __cdecl BppMfCanMuxAudio()
{
    return 1;
}

extern "C" void *__cdecl BppMfGetRenderEventFunc()
{
    return reinterpret_cast<void *>(&RenderEventCallback);
}

extern "C" int __cdecl BppMfCreate(
    const char *outputPath,
    void *sourceTexture,
    int width,
    int height,
    int fps,
    int bitrateBitsPerSecond,
    void **handle)
{
    if (handle == nullptr)
        return -1;
    *handle = nullptr;
    auto *encoder = new Encoder();
    *handle = encoder;
    encoder->fps = fps;

    if (g_unityD3D11 == nullptr || g_unityD3D11->GetDevice() == nullptr)
    {
        SetFailure(encoder, "Unity did not preload the replay plugin through D3D11.");
        return -2;
    }
    if (sourceTexture == nullptr || width <= 0 || height <= 0 ||
        (width & 1) != 0 || (height & 1) != 0 || fps <= 0 || bitrateBitsPerSecond <= 0)
    {
        SetFailure(encoder, "Invalid Media Foundation encoder arguments.");
        return -3;
    }
    const std::wstring path = Utf8ToWide(outputPath);
    if (path.empty())
    {
        SetFailure(encoder, "The output path is not valid UTF-8.");
        return -4;
    }

    std::string startupError;
    if (!StartMediaFoundation(startupError))
    {
        SetFailure(encoder, startupError);
        return -5;
    }
    encoder->mfStarted = true;

    encoder->device = g_unityD3D11->GetDevice();
    encoder->device->GetImmediateContext(&encoder->context);
    encoder->context.As(&encoder->multithread);
    if (encoder->multithread != nullptr)
        encoder->previousMultithreadProtection =
            encoder->multithread->SetMultithreadProtected(TRUE);
    static_cast<ID3D11Texture2D *>(sourceTexture)->QueryInterface(
        IID_PPV_ARGS(&encoder->sourceTexture));
    if (encoder->sourceTexture == nullptr)
    {
        SetFailure(encoder, "Unity native texture pointer is not an ID3D11Texture2D.");
        return -6;
    }
    if (!InitializeVideoProcessor(encoder, width, height))
        return -7;
    if (!InitializeSinkWriter(encoder, path, width, height, bitrateBitsPerSecond))
        return -8;
    return 0;
}

extern "C" int __cdecl BppMfGetSlotCount(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    return encoder == nullptr ? 0 : static_cast<int>(encoder->slots.size());
}

extern "C" int __cdecl BppMfAcquireSlot(void *handle, int *slotIndex)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr || slotIndex == nullptr)
        return 0;
    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (encoder->failed || !encoder->acceptingFrames || encoder->finished)
        return 0;
    for (int index = 0; index < static_cast<int>(encoder->slots.size()); ++index)
    {
        if (encoder->slots[static_cast<size_t>(index)].state != SlotState::Free)
            continue;
        encoder->slots[static_cast<size_t>(index)].state = SlotState::GpuPending;
        *slotIndex = index;
        return 1;
    }
    ++encoder->stats.acquireMisses;
    return 0;
}

extern "C" void *__cdecl BppMfPrepareRenderEvent(
    void *handle,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr || frameCount <= 0)
        return nullptr;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        if (encoder->failed || !encoder->acceptingFrames || encoder->finished ||
            slotIndex < 0 || slotIndex >= static_cast<int>(encoder->slots.size()) ||
            encoder->slots[static_cast<size_t>(slotIndex)].state != SlotState::GpuPending)
            return nullptr;
        ++encoder->renderEventsPending;
    }
    return new RenderEventPacket{encoder, slotIndex, firstFrameIndex, frameCount};
}

extern "C" void __cdecl BppMfCommitRenderEvent(void *eventData)
{
    ReleasePacket(static_cast<RenderEventPacket *>(eventData));
}

extern "C" void __cdecl BppMfCancelRenderEvent(void *eventData)
{
    auto *packet = static_cast<RenderEventPacket *>(eventData);
    if (packet == nullptr)
        return;
    uint8_t expected = 0;
    if (packet->state.compare_exchange_strong(expected, 2))
    {
        ReleasePendingSlot(packet->encoder, packet->slotIndex);
        CompleteRenderEvent(packet->encoder);
    }
    ReleasePacket(packet);
}

extern "C" void __cdecl BppMfDiscardRenderEvent(void *eventData)
{
    auto *packet = static_cast<RenderEventPacket *>(eventData);
    if (packet == nullptr)
        return;
    uint8_t expected = 0;
    if (packet->state.compare_exchange_strong(expected, 2))
    {
        ReleasePendingSlot(packet->encoder, packet->slotIndex);
        CompleteRenderEvent(packet->encoder);
        ReleasePacket(packet);
    }
    ReleasePacket(packet);
}

extern "C" void __cdecl BppMfReleaseSlot(void *handle, int slotIndex)
{
    ReleasePendingSlot(static_cast<Encoder *>(handle), slotIndex);
}

extern "C" int __cdecl BppMfFinish(void *handle, int timeoutMs)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return 0;
    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::milliseconds(std::max(1, timeoutMs));
    {
        std::unique_lock<std::mutex> guard(encoder->mutex);
        if (encoder->finished)
            return encoder->failed ? 0 : 1;
        encoder->acceptingFrames = false;
        if (!encoder->condition.wait_until(
                guard,
                deadline,
                [encoder] { return encoder->renderEventsPending == 0; }))
        {
            encoder->failed = true;
            encoder->finished = true;
            if (encoder->error.empty())
                encoder->error = "Timed out waiting for Unity D3D11 render events.";
            return 0;
        }
    }

    HRESULT result = encoder->sinkWriter == nullptr
        ? E_POINTER
        : encoder->sinkWriter->Finalize();
    if (FAILED(result))
        SetFailure(encoder, HResultMessage("IMFSinkWriter::Finalize", result));
    encoder->sinkWriter.Reset();

    {
        std::unique_lock<std::mutex> guard(encoder->mutex);
        if (!encoder->condition.wait_until(
                guard,
                deadline,
                [encoder] { return encoder->inFlight == 0; }))
        {
            encoder->failed = true;
            if (encoder->error.empty())
                encoder->error = "Timed out waiting for Media Foundation to release NV12 surfaces.";
        }
        encoder->finished = true;
        return encoder->failed ? 0 : 1;
    }
}

extern "C" void __cdecl BppMfDestroy(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return;
    bool canFinish = false;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->acceptingFrames = false;
        canFinish = encoder->renderEventsPending == 0;
    }
    if (canFinish && !encoder->finished && encoder->sinkWriter != nullptr)
        BppMfFinish(encoder, 2000);
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->destroyRequested = true;
    }
    MaybeScheduleDestroy(encoder);
}

extern "C" int __cdecl BppMfIsFailed(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return 1;
    std::lock_guard<std::mutex> guard(encoder->mutex);
    return encoder->failed ? 1 : 0;
}

extern "C" void __cdecl BppMfGetStats(void *handle, BppMfNativeStats *stats)
{
    if (stats == nullptr)
        return;
    std::memset(stats, 0, sizeof(*stats));
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return;
    std::lock_guard<std::mutex> guard(encoder->mutex);
    *stats = encoder->stats;
}

extern "C" int __cdecl BppMfCopyError(void *handle, char *buffer, int capacity)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return CopyText("Native encoder handle is unavailable.", buffer, capacity);
    std::lock_guard<std::mutex> guard(encoder->mutex);
    return CopyText(encoder->error, buffer, capacity);
}

extern "C" int __cdecl BppMfCopyEncoderName(void *handle, char *buffer, int capacity)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return CopyText({}, buffer, capacity);
    std::lock_guard<std::mutex> guard(encoder->mutex);
    return CopyText(encoder->encoderName, buffer, capacity);
}

namespace
{
bool ConfigureAudioReader(
    const std::wstring &path,
    ComPtr<IMFSourceReader> &reader,
    ComPtr<IMFMediaType> &pcmType,
    std::string &error)
{
    HRESULT result = MFCreateSourceReaderFromURL(path.c_str(), nullptr, &reader);
    if (SUCCEEDED(result))
        result = reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
    if (SUCCEEDED(result))
        result = reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), TRUE);
    if (SUCCEEDED(result))
        result = MFCreateMediaType(&pcmType);
    if (SUCCEEDED(result))
        result = pcmType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    if (SUCCEEDED(result))
        result = pcmType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
    if (SUCCEEDED(result))
        result = pcmType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2);
    if (SUCCEEDED(result))
        result = pcmType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48'000);
    if (SUCCEEDED(result))
        result = pcmType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    if (SUCCEEDED(result))
        result = pcmType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
    if (SUCCEEDED(result))
        result = pcmType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 192'000);
    if (SUCCEEDED(result))
        result = reader->SetCurrentMediaType(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
            nullptr,
            pcmType.Get());
    if (FAILED(result))
    {
        error = HResultMessage("Configure WAV source reader", result);
        return false;
    }
    return true;
}

bool ConfigureVideoReader(
    const std::wstring &path,
    ComPtr<IMFSourceReader> &reader,
    ComPtr<IMFMediaType> &videoType,
    std::string &error)
{
    HRESULT result = MFCreateSourceReaderFromURL(path.c_str(), nullptr, &reader);
    if (SUCCEEDED(result))
        result = reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
    if (SUCCEEDED(result))
        result = reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), TRUE);
    if (SUCCEEDED(result))
        result = reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), &videoType);
    GUID major{};
    GUID subtype{};
    if (SUCCEEDED(result))
        result = videoType->GetGUID(MF_MT_MAJOR_TYPE, &major);
    if (SUCCEEDED(result))
        result = videoType->GetGUID(MF_MT_SUBTYPE, &subtype);
    if (SUCCEEDED(result) && (major != MFMediaType_Video || subtype != MFVideoFormat_H264))
        result = MF_E_INVALIDMEDIATYPE;
    if (FAILED(result))
    {
        error = HResultMessage("Open silent H.264 video", result);
        return false;
    }
    return true;
}

bool ConfigureMuxWriter(
    const std::wstring &path,
    IMFMediaType *videoType,
    IMFMediaType *pcmType,
    int audioBitrateBitsPerSecond,
    ComPtr<IMFSinkWriter> &writer,
    DWORD &videoStream,
    DWORD &audioStream,
    std::string &error)
{
    ComPtr<IMFAttributes> attributes;
    HRESULT result = MFCreateAttributes(&attributes, 2);
    if (SUCCEEDED(result))
        result = attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE);
    if (SUCCEEDED(result))
        result = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    if (SUCCEEDED(result))
        result = MFCreateSinkWriterFromURL(path.c_str(), nullptr, attributes.Get(), &writer);
    if (SUCCEEDED(result))
        result = writer->AddStream(videoType, &videoStream);
    if (SUCCEEDED(result))
        result = writer->SetInputMediaType(videoStream, videoType, nullptr);

    ComPtr<IMFMediaType> aacType;
    if (SUCCEEDED(result))
        result = MFCreateMediaType(&aacType);
    if (SUCCEEDED(result))
        result = aacType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    if (SUCCEEDED(result))
        result = aacType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
    if (SUCCEEDED(result))
        result = aacType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2);
    if (SUCCEEDED(result))
        result = aacType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48'000);
    if (SUCCEEDED(result))
        result = aacType->SetUINT32(
            MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
            static_cast<UINT32>(std::max(1, audioBitrateBitsPerSecond) / 8));
    if (SUCCEEDED(result))
        result = aacType->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE, 0);
    if (SUCCEEDED(result))
        result = aacType->SetUINT32(MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29);
    if (SUCCEEDED(result))
        result = writer->AddStream(aacType.Get(), &audioStream);
    if (SUCCEEDED(result))
        result = writer->SetInputMediaType(audioStream, pcmType, nullptr);
    if (SUCCEEDED(result))
        result = writer->BeginWriting();
    if (FAILED(result))
    {
        error = HResultMessage("Create Media Foundation AAC MP4 writer", result);
        return false;
    }
    return true;
}

bool HasTimedOut(
    const std::chrono::steady_clock::time_point &deadline,
    std::string &error)
{
    if (std::chrono::steady_clock::now() < deadline)
        return false;
    error = "Media Foundation audio mux timed out.";
    return true;
}

bool CopyVideoSamples(
    IMFSourceReader *reader,
    IMFSinkWriter *writer,
    DWORD writerStream,
    const std::chrono::steady_clock::time_point &deadline,
    LONGLONG &videoDuration,
    std::string &error)
{
    bool hasStart = false;
    LONGLONG start = 0;
    LONGLONG lastEnd = 0;
    for (;;)
    {
        if (HasTimedOut(deadline, error))
            return false;
        DWORD actualStream = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;
        HRESULT result = reader->ReadSample(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
            0,
            &actualStream,
            &flags,
            &timestamp,
            &sample);
        if (FAILED(result))
        {
            error = HResultMessage("Read H.264 sample", result);
            return false;
        }
        if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            break;
        if (sample == nullptr)
            continue;
        if (!hasStart)
        {
            start = timestamp;
            hasStart = true;
        }
        const LONGLONG normalized = std::max<LONGLONG>(0, timestamp - start);
        LONGLONG duration = 0;
        if (FAILED(sample->GetSampleDuration(&duration)) || duration <= 0)
            duration = 1;
        sample->SetSampleTime(normalized);
        result = writer->WriteSample(writerStream, sample.Get());
        if (FAILED(result))
        {
            error = HResultMessage("Copy H.264 sample", result);
            return false;
        }
        lastEnd = std::max(lastEnd, normalized + duration);
    }
    videoDuration = lastEnd;
    if (!hasStart || videoDuration <= 0)
    {
        error = "The silent H.264 input did not contain a usable video sample.";
        return false;
    }
    return true;
}

bool CopyAudioSamples(
    IMFSourceReader *reader,
    IMFSinkWriter *writer,
    DWORD writerStream,
    LONGLONG videoDuration,
    const std::chrono::steady_clock::time_point &deadline,
    std::string &error)
{
    bool hasStart = false;
    LONGLONG start = 0;
    for (;;)
    {
        if (HasTimedOut(deadline, error))
            return false;
        DWORD actualStream = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;
        HRESULT result = reader->ReadSample(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
            0,
            &actualStream,
            &flags,
            &timestamp,
            &sample);
        if (FAILED(result))
        {
            error = HResultMessage("Read WAV sample", result);
            return false;
        }
        if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            break;
        if (sample == nullptr)
            continue;
        if (!hasStart)
        {
            start = timestamp;
            hasStart = true;
        }
        const LONGLONG normalized = std::max<LONGLONG>(0, timestamp - start);
        if (normalized >= videoDuration)
            break;
        sample->SetSampleTime(normalized);
        result = writer->WriteSample(writerStream, sample.Get());
        if (FAILED(result))
        {
            error = HResultMessage("Encode AAC sample", result);
            return false;
        }
    }
    if (!hasStart)
    {
        error = "The WAV input did not contain a usable audio sample.";
        return false;
    }
    return true;
}
} // namespace

extern "C" int __cdecl BppMfMuxAudio(
    const char *silentVideoPath,
    const char *const *wavPaths,
    int wavPathCount,
    const char *finalPath,
    int audioBitrateBitsPerSecond,
    int timeoutMs,
    char *errorBuffer,
    int errorCapacity)
{
    std::string error;
    if (silentVideoPath == nullptr || finalPath == nullptr || wavPaths == nullptr ||
        wavPathCount != 1 || wavPaths[0] == nullptr)
    {
        CopyText(
            "The Windows native mux currently requires exactly one WASAPI WAV track.",
            errorBuffer,
            errorCapacity);
        return -2;
    }

    const std::wstring videoPath = Utf8ToWide(silentVideoPath);
    const std::wstring wavPath = Utf8ToWide(wavPaths[0]);
    const std::wstring outputPath = Utf8ToWide(finalPath);
    if (videoPath.empty() || wavPath.empty() || outputPath.empty())
    {
        CopyText("A mux input or output path is not valid UTF-8.", errorBuffer, errorCapacity);
        return -3;
    }

    const HRESULT comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    const bool uninitializeCom = SUCCEEDED(comResult);
    if (FAILED(comResult) && comResult != RPC_E_CHANGED_MODE)
    {
        CopyText(HResultMessage("CoInitializeEx", comResult), errorBuffer, errorCapacity);
        return -4;
    }
    if (!StartMediaFoundation(error))
    {
        if (uninitializeCom)
            CoUninitialize();
        CopyText(error, errorBuffer, errorCapacity);
        return -5;
    }

    int returnCode = -6;
    {
        ComPtr<IMFSourceReader> videoReader;
        ComPtr<IMFSourceReader> audioReader;
        ComPtr<IMFMediaType> videoType;
        ComPtr<IMFMediaType> pcmType;
        ComPtr<IMFSinkWriter> writer;
        DWORD videoStream = 0;
        DWORD audioStream = 0;
        const auto deadline = std::chrono::steady_clock::now() +
            std::chrono::milliseconds(std::max(1, timeoutMs));

        if (ConfigureVideoReader(videoPath, videoReader, videoType, error) &&
            ConfigureAudioReader(wavPath, audioReader, pcmType, error) &&
            ConfigureMuxWriter(
                outputPath,
                videoType.Get(),
                pcmType.Get(),
                audioBitrateBitsPerSecond,
                writer,
                videoStream,
                audioStream,
                error))
        {
            LONGLONG videoDuration = 0;
            if (CopyVideoSamples(
                    videoReader.Get(),
                    writer.Get(),
                    videoStream,
                    deadline,
                    videoDuration,
                    error) &&
                CopyAudioSamples(
                    audioReader.Get(),
                    writer.Get(),
                    audioStream,
                    videoDuration,
                    deadline,
                    error))
            {
                if (HasTimedOut(deadline, error))
                {
                    returnCode = -8;
                }
                else
                {
                    const HRESULT result = writer->Finalize();
                    if (SUCCEEDED(result))
                        returnCode = 1;
                    else
                        error = HResultMessage("Finalize AAC MP4", result);
                }
            }
            else if (error == "Media Foundation audio mux timed out.")
            {
                returnCode = -8;
            }
        }
    }

    StopMediaFoundation();
    if (uninitializeCom)
        CoUninitialize();
    CopyText(error, errorBuffer, errorCapacity);
    return returnCode;
}
