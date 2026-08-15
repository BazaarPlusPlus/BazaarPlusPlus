#include "BppReplayMediaFoundation.h"

#include <Windows.h>
#include <d3d11.h>
#include <wrl/client.h>

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

#define UNITY_INTERFACE_API __stdcall

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

struct IUnityGraphicsD3D11 : IUnityInterface
{
    ID3D11Device *(UNITY_INTERFACE_API *GetDevice)();
    ID3D11Resource *(UNITY_INTERFACE_API *TextureFromRenderBuffer)(void *buffer);
    ID3D11Resource *(UNITY_INTERFACE_API *TextureFromNativeTexture)(unsigned int texture);
    ID3D11RenderTargetView *(UNITY_INTERFACE_API *RTVFromRenderBuffer)(void *surface);
    ID3D11ShaderResourceView *(UNITY_INTERFACE_API *SRVFromNativeTexture)(unsigned int texture);
};

extern "C" void __stdcall UnityPluginLoad(IUnityInterfaces *interfaces);
extern "C" void __stdcall UnityPluginUnload();

namespace
{
ComPtr<ID3D11Device> g_device;
IUnityGraphicsD3D11 g_d3dInterface{};

ID3D11Device *UNITY_INTERFACE_API GetDevice()
{
    return g_device.Get();
}

IUnityInterface *UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid)
{
    if (guid.high == 0xAAB37EF87A87D748ULL && guid.low == 0xBF76967F07EFB177ULL)
        return &g_d3dInterface;
    return nullptr;
}

IUnityInterface *UNITY_INTERFACE_API GetInterfaceSplit(
    unsigned long long high,
    unsigned long long low)
{
    return GetInterface({high, low});
}

void UNITY_INTERFACE_API RegisterInterface(UnityInterfaceGUID, IUnityInterface *)
{
}

void UNITY_INTERFACE_API RegisterInterfaceSplit(
    unsigned long long,
    unsigned long long,
    IUnityInterface *)
{
}

bool WriteSilentWav(const std::filesystem::path &path, int seconds)
{
    constexpr uint32_t sampleRate = 48'000;
    constexpr uint16_t channels = 2;
    constexpr uint16_t bits = 16;
    const uint32_t dataBytes = sampleRate * channels * (bits / 8) * seconds;
    const uint32_t riffBytes = 36 + dataBytes;
    const uint32_t byteRate = sampleRate * channels * (bits / 8);
    const uint16_t blockAlign = channels * (bits / 8);
    std::ofstream output(path, std::ios::binary);
    if (!output)
        return false;
    output.write("RIFF", 4);
    output.write(reinterpret_cast<const char *>(&riffBytes), sizeof(riffBytes));
    output.write("WAVEfmt ", 8);
    const uint32_t formatSize = 16;
    const uint16_t pcm = 1;
    output.write(reinterpret_cast<const char *>(&formatSize), sizeof(formatSize));
    output.write(reinterpret_cast<const char *>(&pcm), sizeof(pcm));
    output.write(reinterpret_cast<const char *>(&channels), sizeof(channels));
    output.write(reinterpret_cast<const char *>(&sampleRate), sizeof(sampleRate));
    output.write(reinterpret_cast<const char *>(&byteRate), sizeof(byteRate));
    output.write(reinterpret_cast<const char *>(&blockAlign), sizeof(blockAlign));
    output.write(reinterpret_cast<const char *>(&bits), sizeof(bits));
    output.write("data", 4);
    output.write(reinterpret_cast<const char *>(&dataBytes), sizeof(dataBytes));
    std::vector<char> silence(dataBytes, 0);
    output.write(silence.data(), static_cast<std::streamsize>(silence.size()));
    return output.good();
}
} // namespace

int main()
{
    ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL featureLevel{};
    HRESULT result = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        nullptr,
        0,
        D3D11_SDK_VERSION,
        &g_device,
        &featureLevel,
        &context);
    if (FAILED(result))
    {
        std::fprintf(stderr, "D3D11CreateDevice failed: 0x%08lX\n", static_cast<unsigned long>(result));
        return 1;
    }

    g_d3dInterface.GetDevice = &GetDevice;
    IUnityInterfaces interfaces{
        &GetInterface,
        &RegisterInterface,
        &GetInterfaceSplit,
        &RegisterInterfaceSplit,
    };
    UnityPluginLoad(&interfaces);

    constexpr int width = 1280;
    constexpr int height = 720;
    constexpr int fps = 60;
    D3D11_TEXTURE2D_DESC description{};
    description.Width = width;
    description.Height = height;
    description.MipLevels = 1;
    description.ArraySize = 1;
    // Typeless on purpose: it forces the typed-alias path in InitializeVideoProcessor, which is
    // what Unity actually hands the plugin.
    description.Format = DXGI_FORMAT_R8G8B8A8_TYPELESS;
    description.SampleDesc.Count = 1;
    description.Usage = D3D11_USAGE_DEFAULT;
    description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    ComPtr<ID3D11Texture2D> source;
    result = g_device->CreateTexture2D(&description, nullptr, &source);
    if (FAILED(result))
        return 2;

    ComPtr<ID3D11RenderTargetView> target;
    D3D11_RENDER_TARGET_VIEW_DESC targetDescription{};
    targetDescription.Format = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    targetDescription.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
    result = g_device->CreateRenderTargetView(source.Get(), &targetDescription, &target);
    if (FAILED(result))
        return 3;
    const float color[4]{0.15F, 0.35F, 0.75F, 1.0F};
    context->ClearRenderTargetView(target.Get(), color);

    const auto root = std::filesystem::temp_directory_path() / "bpp-mf-smoke";
    std::filesystem::create_directories(root);
    const auto silentPath = root / "silent.mp4";
    const auto wavPath = root / "audio.wav";
    const auto finalPath = root / "final.mp4";
    std::filesystem::remove(silentPath);
    std::filesystem::remove(finalPath);

    void *encoder = nullptr;
    const std::string silentUtf8 = silentPath.u8string();
    int createResult = BppMfCreate(
        silentUtf8.c_str(),
        source.Get(),
        width,
        height,
        fps,
        6'000'000,
        &encoder);
    char error[2048]{};
    BppMfCopyError(encoder, error, static_cast<int>(sizeof(error)));
    if (createResult != 0)
    {
        std::fprintf(stderr, "BppMfCreate failed (%d): %s\n", createResult, error);
        BppMfDestroy(encoder);
        return 4;
    }

    auto renderEvent = reinterpret_cast<void(__stdcall *)(int, void *)>(BppMfGetRenderEventFunc());
    for (int frame = 0; frame < fps * 2; ++frame)
    {
        int slot = -1;
        for (int retry = 0; retry < 500 && BppMfAcquireSlot(encoder, &slot) == 0; ++retry)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        if (slot < 0)
        {
            BppMfCopyError(encoder, error, static_cast<int>(sizeof(error)));
            std::fprintf(stderr, "No slot available at frame %d: %s\n", frame, error);
            return 5;
        }
        void *packet = BppMfPrepareRenderEvent(encoder, slot, frame, 1);
        if (packet == nullptr)
            return 6;
        renderEvent(1, packet);
        BppMfCommitRenderEvent(packet);
    }

    if (BppMfFinish(encoder, 30'000) == 0)
    {
        BppMfCopyError(encoder, error, static_cast<int>(sizeof(error)));
        std::fprintf(stderr, "BppMfFinish failed: %s\n", error);
        return 7;
    }
    char encoderName[512]{};
    BppMfCopyEncoderName(encoder, encoderName, static_cast<int>(sizeof(encoderName)));
    BppMfNativeStats stats{};
    BppMfGetStats(encoder, &stats);
    std::printf(
        "encoder=%s submitted=%d written=%d max_in_flight=%d\n",
        encoderName,
        stats.submittedFrames,
        stats.writtenFrames,
        stats.maxInFlight);
    BppMfDestroy(encoder);

    if (!WriteSilentWav(wavPath, 2))
        return 8;
    const std::string wavUtf8 = wavPath.u8string();
    const std::string finalUtf8 = finalPath.u8string();
    const char *wavPaths[]{wavUtf8.c_str()};
    const auto muxStarted = std::chrono::steady_clock::now();
    const int muxResult = BppMfMuxAudio(
        silentUtf8.c_str(),
        wavPaths,
        1,
        finalUtf8.c_str(),
        192'000,
        30'000,
        error,
        static_cast<int>(sizeof(error)));
    const auto muxElapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - muxStarted);
    if (muxResult != 1)
    {
        std::fprintf(stderr, "BppMfMuxAudio failed (%d): %s\n", muxResult, error);
        return 9;
    }
    if (muxElapsed > std::chrono::seconds(3))
    {
        std::fprintf(
            stderr,
            "BppMfMuxAudio was throttled (%lld ms).\n",
            static_cast<long long>(muxElapsed.count()));
        return 10;
    }
    std::printf(
        "mux_ms=%lld\nsilent=%s\nfinal=%s\n",
        static_cast<long long>(muxElapsed.count()),
        silentUtf8.c_str(),
        finalUtf8.c_str());
    UnityPluginUnload();
    return 0;
}
