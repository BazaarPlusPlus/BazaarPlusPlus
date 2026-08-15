#import <AVFoundation/AVFoundation.h>
#import <CoreVideo/CoreVideo.h>
#import <Metal/Metal.h>
#import <VideoToolbox/VideoToolbox.h>

#include "BppReplayVideoToolbox.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#define UNITY_INTERFACE_API
#define UNITY_INTERFACE_EXPORT __attribute__((visibility("default")))

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

struct IUnityGraphicsMetal : IUnityInterface
{
    NSBundle *(UNITY_INTERFACE_API *MetalBundle)();
    id<MTLDevice>(UNITY_INTERFACE_API *MetalDevice)();
    id<MTLCommandBuffer>(UNITY_INTERFACE_API *CurrentCommandBuffer)();
    id<MTLCommandEncoder>(UNITY_INTERFACE_API *CurrentCommandEncoder)();
    void(UNITY_INTERFACE_API *EndCurrentCommandEncoder)();
    MTLRenderPassDescriptor *(UNITY_INTERFACE_API *CurrentRenderPassDescriptor)();
    UnityRenderBuffer(UNITY_INTERFACE_API *RenderBufferFromHandle)(void *bufferHandle);
    id<MTLTexture>(UNITY_INTERFACE_API *TextureFromRenderBuffer)(UnityRenderBuffer buffer);
    id<MTLTexture>(UNITY_INTERFACE_API *AAResolvedTextureFromRenderBuffer)(UnityRenderBuffer buffer);
    id<MTLTexture>(UNITY_INTERFACE_API *StencilTextureFromRenderBuffer)(UnityRenderBuffer buffer);
};

namespace
{
constexpr int kSlotCount = 8;

constexpr const char *kBgraToNv12MetalSource = R"METAL(
#include <metal_stdlib>
using namespace metal;

struct ConversionParameters
{
    uint width;
    uint height;
    uint sourceIsLinear;
    uint reserved;
};

inline float encodeSrgb(float value)
{
    value = clamp(value, 0.0f, 1.0f);
    return value <= 0.0031308f
        ? 12.92f * value
        : 1.055f * pow(value, 1.0f / 2.4f) - 0.055f;
}

inline float3 videoRgb(float3 value, uint sourceIsLinear)
{
    if (sourceIsLinear == 0)
        return clamp(value, 0.0f, 1.0f);
    return float3(encodeSrgb(value.r), encodeSrgb(value.g), encodeSrgb(value.b));
}

inline float luma709(float3 rgb)
{
    return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

inline float videoRangeLuma(float3 rgb)
{
    return clamp((16.0f + 219.0f * luma709(rgb)) / 255.0f, 0.0f, 1.0f);
}

inline uint2 sourcePositionForOutput(
    uint2 outputPosition,
    uint2 outputSize,
    uint2 sourceSize)
{
    if (all(outputSize == sourceSize))
        return outputPosition;

    return min((outputPosition * sourceSize) / outputSize, sourceSize - uint2(1));
}

kernel void bppBgraToNv12(
    texture2d<float, access::read> source [[texture(0)]],
    texture2d<float, access::write> lumaPlane [[texture(1)]],
    texture2d<float, access::write> chromaPlane [[texture(2)]],
    constant ConversionParameters &parameters [[buffer(0)]],
    uint2 chromaPosition [[thread_position_in_grid]])
{
    uint2 outputPosition = chromaPosition * 2;
    uint2 outputSize = uint2(parameters.width, parameters.height);
    if (outputPosition.x >= outputSize.x || outputPosition.y >= outputSize.y)
        return;

    uint2 sourceSize = uint2(source.get_width(), source.get_height());
    uint2 source00 = sourcePositionForOutput(outputPosition, outputSize, sourceSize);
    uint2 source10 = sourcePositionForOutput(outputPosition + uint2(1, 0), outputSize, sourceSize);
    uint2 source01 = sourcePositionForOutput(outputPosition + uint2(0, 1), outputSize, sourceSize);
    uint2 source11 = sourcePositionForOutput(outputPosition + uint2(1, 1), outputSize, sourceSize);

    float3 rgb00 = videoRgb(source.read(source00).rgb, parameters.sourceIsLinear);
    float3 rgb10 = videoRgb(source.read(source10).rgb, parameters.sourceIsLinear);
    float3 rgb01 = videoRgb(source.read(source01).rgb, parameters.sourceIsLinear);
    float3 rgb11 = videoRgb(source.read(source11).rgb, parameters.sourceIsLinear);

    lumaPlane.write(float4(videoRangeLuma(rgb00), 0.0f, 0.0f, 1.0f), outputPosition);
    lumaPlane.write(float4(videoRangeLuma(rgb10), 0.0f, 0.0f, 1.0f), outputPosition + uint2(1, 0));
    lumaPlane.write(float4(videoRangeLuma(rgb01), 0.0f, 0.0f, 1.0f), outputPosition + uint2(0, 1));
    lumaPlane.write(float4(videoRangeLuma(rgb11), 0.0f, 0.0f, 1.0f), outputPosition + uint2(1, 1));

    float3 averageRgb = (rgb00 + rgb10 + rgb01 + rgb11) * 0.25f;
    float averageLuma = luma709(averageRgb);
    float cb = (128.0f + 224.0f * (averageRgb.b - averageLuma) / 1.8556f) / 255.0f;
    float cr = (128.0f + 224.0f * (averageRgb.r - averageLuma) / 1.5748f) / 255.0f;
    chromaPlane.write(float4(clamp(cb, 0.0f, 1.0f), clamp(cr, 0.0f, 1.0f), 0.0f, 1.0f), chromaPosition);
}
)METAL";

enum class SlotState : uint8_t
{
    Free,
    GpuPending,
    Encoding,
};

struct FrameSlot
{
    CVPixelBufferRef pixelBuffer = nullptr;
    CVMetalTextureRef lumaTexture = nullptr;
    CVMetalTextureRef chromaTexture = nullptr;
    SlotState state = SlotState::Free;
};

struct Encoder
{
    std::mutex mutex;
    std::string error;
    bool failed = false;
    bool acceptingFrames = true;
    bool finished = false;
    int fps = 60;
    int inFlight = 0;
    int renderEventsPending = 0;
    int64_t lastFrameIndex = -1;
    BppVtNativeStats stats{};
    std::condition_variable renderEventCondition;
    std::condition_variable writerCondition;
    std::vector<FrameSlot> slots;

    VTCompressionSessionRef compressionSession = nullptr;
    CVPixelBufferPoolRef pixelBufferPool = nullptr;
    CVMetalTextureCacheRef metalTextureCache = nullptr;
    id<MTLComputePipelineState> __strong conversionPipeline = nil;
    AVAssetWriter *__strong writer = nil;
    AVAssetWriterInput *__strong writerInput = nil;
    dispatch_queue_t encodeQueue = nullptr;
    dispatch_queue_t writerQueue = nullptr;
    bool writerStarted = false;
    bool writerClosed = false;
};

struct RenderEventPacket
{
    Encoder *encoder;
    int slotIndex;
    int64_t firstFrameIndex;
    int frameCount;
    std::atomic<int> references{2};
    std::atomic<uint8_t> state{0};
};

struct ConversionParameters
{
    uint32_t width;
    uint32_t height;
    uint32_t sourceIsLinear;
    uint32_t reserved;
};

struct FrameSubmission
{
    Encoder *encoder;
    int slotIndex;
    std::atomic<int> remainingFrames;
};

IUnityGraphicsMetal *gUnityMetal = nullptr;
std::mutex gConversionPipelineMutex;
NSMapTable<id<MTLDevice>, id<MTLComputePipelineState>> *__strong
    gConversionPipelines = nil;

void ReleaseRenderEventPacket(RenderEventPacket *packet)
{
    if (packet != nullptr && packet->references.fetch_sub(1, std::memory_order_acq_rel) == 1)
        delete packet;
}

struct ScopedRenderEventPacketReference
{
    RenderEventPacket *packet;

    ~ScopedRenderEventPacketReference()
    {
        ReleaseRenderEventPacket(packet);
    }
};

void SetFailure(Encoder *encoder, const std::string &message)
{
    if (encoder == nullptr)
        return;

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->failed = true;
        if (encoder->error.empty())
            encoder->error = message;
    }
    encoder->writerCondition.notify_all();
}

std::string StatusMessage(const char *operation, OSStatus status)
{
    char buffer[160];
    std::snprintf(buffer, sizeof(buffer), "%s failed with OSStatus %d", operation, (int)status);
    return std::string(buffer);
}

void ReleaseSlot(Encoder *encoder, int slotIndex)
{
    if (encoder == nullptr)
        return;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (slotIndex >= 0 && slotIndex < (int)encoder->slots.size())
        encoder->slots[(size_t)slotIndex].state = SlotState::Free;
    encoder->inFlight = std::max(0, encoder->inFlight - 1);
}

void CompleteSubmissionFrame(FrameSubmission *submission)
{
    if (submission == nullptr)
        return;

    if (submission->remainingFrames.fetch_sub(1, std::memory_order_acq_rel) != 1)
        return;

    ReleaseSlot(submission->encoder, submission->slotIndex);
    delete submission;
}

bool StartWriterIfNeeded(Encoder *encoder, CMSampleBufferRef sampleBuffer)
{
    if (encoder->writerClosed)
        return false;
    if (encoder->writerStarted)
        return true;

    CMFormatDescriptionRef formatDescription = CMSampleBufferGetFormatDescription(sampleBuffer);
    if (formatDescription == nullptr)
    {
        SetFailure(encoder, "VideoToolbox returned a sample without a format description.");
        return false;
    }

    AVAssetWriterInput *input = [[AVAssetWriterInput alloc]
        initWithMediaType:AVMediaTypeVideo
        outputSettings:nil
        sourceFormatHint:formatDescription];
    input.expectsMediaDataInRealTime = YES;

    if (![encoder->writer canAddInput:input])
    {
        SetFailure(encoder, "AVAssetWriter rejected the VideoToolbox passthrough input.");
        return false;
    }
    [encoder->writer addInput:input];
    encoder->writerInput = input;

    if (![encoder->writer startWriting])
    {
        NSString *message = encoder->writer.error.localizedDescription;
        SetFailure(
            encoder,
            message == nil ? "AVAssetWriter failed to start."
                           : std::string(message.UTF8String));
        return false;
    }

    [encoder->writer startSessionAtSourceTime:kCMTimeZero];
    encoder->writerStarted = true;
    return true;
}

void CompressionOutputCallback(
    void *outputCallbackRefCon,
    void *sourceFrameRefCon,
    OSStatus status,
    VTEncodeInfoFlags infoFlags,
    CMSampleBufferRef sampleBuffer)
{
    auto *encoder = static_cast<Encoder *>(outputCallbackRefCon);
    auto *submission = static_cast<FrameSubmission *>(sourceFrameRefCon);

    if (encoder == nullptr || submission == nullptr)
        return;
    if (status != noErr || sampleBuffer == nullptr || (infoFlags & kVTEncodeInfo_FrameDropped) != 0)
    {
        {
            std::lock_guard<std::mutex> guard(encoder->mutex);
            encoder->stats.encodeErrors++;
        }
        SetFailure(
            encoder,
            status == noErr ? "VideoToolbox dropped an encoded frame."
                            : StatusMessage("VTCompressionSession output", status));
        CompleteSubmissionFrame(submission);
        return;
    }

    CFRetain(sampleBuffer);
    CompleteSubmissionFrame(submission);
    dispatch_async(encoder->writerQueue, ^{
      @autoreleasepool
      {
          bool appended = false;
          if (!encoder->writerClosed && StartWriterIfNeeded(encoder, sampleBuffer))
          {
              if (encoder->writerInput.readyForMoreMediaData)
              {
                  appended = [encoder->writerInput appendSampleBuffer:sampleBuffer];
                  if (!appended)
                  {
                      NSString *message = encoder->writer.error.localizedDescription;
                      SetFailure(
                          encoder,
                          message == nil ? "AVAssetWriter failed to append an encoded frame."
                                         : std::string(message.UTF8String));
                  }
              }
                  else
                  {
                      {
                          std::lock_guard<std::mutex> guard(encoder->mutex);
                          encoder->stats.notReadyDrops++;
                      }
                      SetFailure(
                          encoder,
                          "AVAssetWriter input backpressure dropped an encoded video frame.");
                  }
          }

          if (appended)
          {
              {
                  std::lock_guard<std::mutex> guard(encoder->mutex);
                  encoder->stats.appendedFrames++;
              }
              encoder->writerCondition.notify_all();
          }

          CFRelease(sampleBuffer);
      }
    });
}

void ReleaseGpuPendingSlot(Encoder *encoder, int slotIndex)
{
    if (encoder == nullptr)
        return;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (slotIndex < 0 || slotIndex >= (int)encoder->slots.size())
        return;
    auto &slot = encoder->slots[(size_t)slotIndex];
    if (slot.state == SlotState::GpuPending)
        slot.state = SlotState::Free;
}

int SubmitFramesInternal(
    Encoder *encoder,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount)
{
    if (
        encoder == nullptr || slotIndex < 0 || slotIndex >= (int)encoder->slots.size() ||
        frameCount <= 0)
        return 0;

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        auto &slot = encoder->slots[(size_t)slotIndex];
        if (encoder->failed || encoder->finished || slot.state != SlotState::GpuPending)
            return 0;
        slot.state = SlotState::Encoding;
        encoder->inFlight++;
        encoder->stats.maxInFlight = std::max(encoder->stats.maxInFlight, encoder->inFlight);
        encoder->stats.submittedFrames += frameCount;
        encoder->lastFrameIndex = std::max(
            encoder->lastFrameIndex,
            firstFrameIndex + frameCount - 1);
    }

    auto *submission = new FrameSubmission{encoder, slotIndex, frameCount};
    CMTime duration = CMTimeMake(1, encoder->fps);
    for (int offset = 0; offset < frameCount; offset++)
    {
        CMTime presentationTime = CMTimeMake(firstFrameIndex + offset, encoder->fps);
        VTEncodeInfoFlags infoFlags = 0;
        OSStatus status = VTCompressionSessionEncodeFrame(
            encoder->compressionSession,
            encoder->slots[(size_t)slotIndex].pixelBuffer,
            presentationTime,
            duration,
            nullptr,
            submission,
            &infoFlags);
        if (status == noErr)
            continue;

        {
            std::lock_guard<std::mutex> guard(encoder->mutex);
            encoder->stats.encodeErrors++;
        }
        SetFailure(encoder, StatusMessage("VTCompressionSessionEncodeFrame", status));
        for (int unsubmitted = offset; unsubmitted < frameCount; unsubmitted++)
            CompleteSubmissionFrame(submission);
        return 0;
    }

    return 1;
}

void CompleteRenderEvent(Encoder *encoder)
{
    if (encoder == nullptr)
        return;

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->renderEventsPending = std::max(0, encoder->renderEventsPending - 1);
    }
    encoder->renderEventCondition.notify_all();
}

void UNITY_INTERFACE_API RenderEventCallback(int eventId, void *data)
{
    (void)eventId;
    auto *packet = static_cast<RenderEventPacket *>(data);
    if (packet == nullptr)
        return;

    ScopedRenderEventPacketReference callbackReference{packet};
    uint8_t expectedState = 0;
    if (!packet->state.compare_exchange_strong(
            expectedState,
            1,
            std::memory_order_acq_rel))
    {
        return;
    }

    Encoder *encoder = packet->encoder;
    const int slotIndex = packet->slotIndex;
    const int64_t firstFrameIndex = packet->firstFrameIndex;
    const int frameCount = packet->frameCount;

    IUnityGraphicsMetal *metal = gUnityMetal;
    MTLRenderPassDescriptor *renderPass =
        metal == nullptr ? nil : metal->CurrentRenderPassDescriptor();
    MTLRenderPassColorAttachmentDescriptor *colorAttachment =
        renderPass == nil ? nil : renderPass.colorAttachments[0];
    id<MTLTexture> sourceTexture = colorAttachment.resolveTexture;
    if (sourceTexture == nil)
        sourceTexture = colorAttachment.texture;
    id<MTLTexture> lumaTexture = CVMetalTextureGetTexture(
        encoder->slots[(size_t)slotIndex].lumaTexture);
    id<MTLTexture> chromaTexture = CVMetalTextureGetTexture(
        encoder->slots[(size_t)slotIndex].chromaTexture);
    id<MTLCommandBuffer> commandBuffer =
        metal == nullptr ? nil : metal->CurrentCommandBuffer();
    if (
        commandBuffer == nil || sourceTexture == nil || lumaTexture == nil ||
        chromaTexture == nil || encoder->conversionPipeline == nil)
    {
        SetFailure(
            encoder,
            "Unity render event exposed no final Metal render target, NV12 plane, or command buffer.");
        ReleaseGpuPendingSlot(encoder, slotIndex);
        CompleteRenderEvent(encoder);
        return;
    }
    if (
        chromaTexture.width * 2 != lumaTexture.width ||
        chromaTexture.height * 2 != lumaTexture.height || sourceTexture.sampleCount != 1)
    {
        char dimensions[260];
        std::snprintf(
            dimensions,
            sizeof(dimensions),
            "Unity final Metal target is %lux%lu samples=%lu; NV12 planes are inconsistent at %lux%lu and %lux%lu.",
            (unsigned long)sourceTexture.width,
            (unsigned long)sourceTexture.height,
            (unsigned long)sourceTexture.sampleCount,
            (unsigned long)lumaTexture.width,
            (unsigned long)lumaTexture.height,
            (unsigned long)chromaTexture.width,
            (unsigned long)chromaTexture.height);
        SetFailure(encoder, dimensions);
        ReleaseGpuPendingSlot(encoder, slotIndex);
        CompleteRenderEvent(encoder);
        return;
    }

    bool sourceIsLinear = false;
    switch (sourceTexture.pixelFormat)
    {
    case MTLPixelFormatBGRA8Unorm:
    case MTLPixelFormatRGBA8Unorm:
        sourceIsLinear = false;
        break;
    case MTLPixelFormatBGRA8Unorm_sRGB:
    case MTLPixelFormatRGBA8Unorm_sRGB:
    case MTLPixelFormatRGBA16Float:
    case MTLPixelFormatRGBA32Float:
        sourceIsLinear = true;
        break;
    default:
    {
        char format[160];
        std::snprintf(
            format,
            sizeof(format),
            "Unsupported Unity final Metal pixel format %lu for NV12 conversion.",
            (unsigned long)sourceTexture.pixelFormat);
        SetFailure(encoder, format);
        ReleaseGpuPendingSlot(encoder, slotIndex);
        CompleteRenderEvent(encoder);
        return;
    }
    }

    metal->EndCurrentCommandEncoder();
    id<MTLComputeCommandEncoder> computeEncoder = [commandBuffer computeCommandEncoder];
    if (computeEncoder == nil)
    {
        SetFailure(encoder, "Metal could not create the NV12 conversion encoder.");
        ReleaseGpuPendingSlot(encoder, slotIndex);
        CompleteRenderEvent(encoder);
        return;
    }

    [computeEncoder setComputePipelineState:encoder->conversionPipeline];
    [computeEncoder setTexture:sourceTexture atIndex:0];
    [computeEncoder setTexture:lumaTexture atIndex:1];
    [computeEncoder setTexture:chromaTexture atIndex:2];
    ConversionParameters parameters{
        (uint32_t)lumaTexture.width,
        (uint32_t)lumaTexture.height,
        sourceIsLinear ? 1u : 0u,
        0u,
    };
    [computeEncoder setBytes:&parameters length:sizeof(parameters) atIndex:0];

    const NSUInteger threadWidth = encoder->conversionPipeline.threadExecutionWidth;
    const NSUInteger threadHeight = std::max(
        (NSUInteger)1,
        encoder->conversionPipeline.maxTotalThreadsPerThreadgroup / threadWidth);
    [computeEncoder
        dispatchThreads:MTLSizeMake(chromaTexture.width, chromaTexture.height, 1)
        threadsPerThreadgroup:MTLSizeMake(threadWidth, threadHeight, 1)];
    [computeEncoder endEncoding];

    [commandBuffer addCompletedHandler:^(id<MTLCommandBuffer> completedCommandBuffer) {
      @autoreleasepool
      {
          if (completedCommandBuffer.status == MTLCommandBufferStatusCompleted)
          {
              dispatch_async(encoder->encodeQueue, ^{
                @autoreleasepool
                {
                    if (!SubmitFramesInternal(
                            encoder,
                            slotIndex,
                            firstFrameIndex,
                            frameCount))
                    {
                        ReleaseGpuPendingSlot(encoder, slotIndex);
                    }
                    CompleteRenderEvent(encoder);
                }
              });
              return;
          }
          else
          {
              NSString *message = completedCommandBuffer.error.localizedDescription;
              SetFailure(
                  encoder,
                  message == nil ? "Unity Metal command buffer did not complete."
                                 : std::string(message.UTF8String));
              ReleaseGpuPendingSlot(encoder, slotIndex);
          }
          CompleteRenderEvent(encoder);
      }
    }];
}

bool SetCompressionProperty(
    Encoder *encoder,
    CFStringRef key,
    CFTypeRef value,
    const char *name)
{
    OSStatus status = VTSessionSetProperty(encoder->compressionSession, key, value);
    if (status == noErr)
        return true;

    SetFailure(encoder, StatusMessage(name, status));
    return false;
}

bool InitializeConversionPipeline(Encoder *encoder, id<MTLDevice> device)
{
    std::lock_guard<std::mutex> cacheGuard(gConversionPipelineMutex);
    if (gConversionPipelines == nil)
    {
        gConversionPipelines = [NSMapTable
            mapTableWithKeyOptions:NSPointerFunctionsWeakMemory |
                NSPointerFunctionsObjectPointerPersonality
            valueOptions:NSPointerFunctionsStrongMemory];
    }
    id<MTLComputePipelineState> cachedPipeline =
        [gConversionPipelines objectForKey:device];
    if (cachedPipeline != nil)
    {
        encoder->conversionPipeline = cachedPipeline;
        return true;
    }

    NSError *libraryError = nil;
    NSString *source = [NSString stringWithUTF8String:kBgraToNv12MetalSource];
    id<MTLLibrary> library = [device newLibraryWithSource:source options:nil error:&libraryError];
    if (library == nil)
    {
        NSString *message = libraryError.localizedDescription;
        SetFailure(
            encoder,
            message == nil ? "Metal could not compile the NV12 conversion shader."
                           : std::string(message.UTF8String));
        return false;
    }

    id<MTLFunction> function = [library newFunctionWithName:@"bppBgraToNv12"];
    if (function == nil)
    {
        SetFailure(encoder, "Metal library did not contain the NV12 conversion kernel.");
        return false;
    }

    NSError *pipelineError = nil;
    encoder->conversionPipeline = [device
        newComputePipelineStateWithFunction:function
        error:&pipelineError];
    if (encoder->conversionPipeline == nil)
    {
        NSString *message = pipelineError.localizedDescription;
        SetFailure(
            encoder,
            message == nil ? "Metal could not create the NV12 conversion pipeline."
                           : std::string(message.UTF8String));
        return false;
    }
    [gConversionPipelines setObject:encoder->conversionPipeline forKey:device];
    return true;
}

void CancelWriterOnQueue(Encoder *encoder)
{
    if (encoder == nullptr || encoder->writerQueue == nullptr)
        return;
    dispatch_sync(encoder->writerQueue, ^{
      @autoreleasepool
      {
          encoder->writerClosed = true;
          @try
          {
              if (encoder->writer.status == AVAssetWriterStatusWriting)
                  [encoder->writer cancelWriting];
          }
          @catch (NSException *exception)
          {
              SetFailure(
                  encoder,
                  exception.reason == nil
                      ? "AVAssetWriter threw while cancelling."
                      : std::string(exception.reason.UTF8String));
          }
      }
    });
}

void DestroyEncoder(Encoder *encoder)
{
    if (encoder == nullptr)
        return;

    if (encoder->compressionSession != nullptr)
    {
        VTCompressionSessionInvalidate(encoder->compressionSession);
        CFRelease(encoder->compressionSession);
        encoder->compressionSession = nullptr;
    }
    for (auto &slot : encoder->slots)
    {
        if (slot.lumaTexture != nullptr)
            CFRelease(slot.lumaTexture);
        if (slot.chromaTexture != nullptr)
            CFRelease(slot.chromaTexture);
        if (slot.pixelBuffer != nullptr)
            CFRelease(slot.pixelBuffer);
        slot.lumaTexture = nullptr;
        slot.chromaTexture = nullptr;
        slot.pixelBuffer = nullptr;
    }
    encoder->slots.clear();

    if (encoder->metalTextureCache != nullptr)
        CFRelease(encoder->metalTextureCache);
    if (encoder->pixelBufferPool != nullptr)
        CFRelease(encoder->pixelBufferPool);
    encoder->metalTextureCache = nullptr;
    encoder->pixelBufferPool = nullptr;
    encoder->conversionPipeline = nil;
    encoder->writerInput = nil;
    encoder->writer = nil;
}
} // namespace

extern "C"
{
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces *interfaces)
{
    if (interfaces == nullptr)
    {
        gUnityMetal = nullptr;
        return;
    }
    gUnityMetal = reinterpret_cast<IUnityGraphicsMetal *>(
        interfaces->GetInterfaceSplit(0x992C8EAEA95811E5ULL, 0x9A62C4B5B9876117ULL));
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    gUnityMetal = nullptr;
}

__attribute__((visibility("default"))) int BppVtHasUnityMetalInterface()
{
    return gUnityMetal == nullptr ? 0 : 1;
}

__attribute__((visibility("default"))) void *BppVtGetRenderEventFunc()
{
    return reinterpret_cast<void *>(&RenderEventCallback);
}

__attribute__((visibility("default"))) int BppVtCreate(
    const char *outputPath,
    int width,
    int height,
    int fps,
    int bitrateBitsPerSecond,
    void **handle)
{
    if (handle == nullptr)
        return -1;
    *handle = nullptr;
    if (outputPath == nullptr || width <= 0 || height <= 0 || fps <= 0 || bitrateBitsPerSecond <= 0)
        return -2;

    @autoreleasepool
    {
        auto *encoder = new Encoder();
        encoder->fps = fps;
        encoder->encodeQueue = dispatch_queue_create(
            "com.bazaarplusplus.replay.videotoolbox.encode",
            DISPATCH_QUEUE_SERIAL);
        encoder->writerQueue = dispatch_queue_create(
            "com.bazaarplusplus.replay.videotoolbox.writer",
            DISPATCH_QUEUE_SERIAL);

        NSString *path = [NSString stringWithUTF8String:outputPath];
        if (path == nil)
        {
            delete encoder;
            return -3;
        }

        NSURL *url = [NSURL fileURLWithPath:path];
        [[NSFileManager defaultManager] removeItemAtURL:url error:nil];
        NSError *writerError = nil;
        encoder->writer = [[AVAssetWriter alloc]
            initWithURL:url
            fileType:AVFileTypeMPEG4
            error:&writerError];
        if (encoder->writer == nil)
        {
            encoder->error = writerError == nil
                ? "AVAssetWriter initialization failed."
                : std::string(writerError.localizedDescription.UTF8String);
            encoder->failed = true;
            *handle = encoder;
            return -4;
        }

        NSDictionary *pixelBufferAttributes = @{
            (NSString *)kCVPixelBufferPixelFormatTypeKey :
                @(kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange),
            (NSString *)kCVPixelBufferWidthKey : @(width),
            (NSString *)kCVPixelBufferHeightKey : @(height),
            (NSString *)kCVPixelBufferMetalCompatibilityKey : @YES,
            (NSString *)kCVPixelBufferIOSurfacePropertiesKey : @{},
        };
        NSDictionary *poolAttributes = @{
            (NSString *)kCVPixelBufferPoolMinimumBufferCountKey : @(kSlotCount),
        };

        CVReturn cvStatus = CVPixelBufferPoolCreate(
            kCFAllocatorDefault,
            (__bridge CFDictionaryRef)poolAttributes,
            (__bridge CFDictionaryRef)pixelBufferAttributes,
            &encoder->pixelBufferPool);
        if (cvStatus != kCVReturnSuccess || encoder->pixelBufferPool == nullptr)
        {
            SetFailure(encoder, StatusMessage("CVPixelBufferPoolCreate", cvStatus));
            *handle = encoder;
            return -5;
        }

        // Use Unity's device rather than the process default. The IOSurface-backed destination
        // textures must belong to the same Metal device as Unity's render command buffer.
        id<MTLDevice> device = gUnityMetal == nullptr ? nil : gUnityMetal->MetalDevice();
        if (device == nil)
        {
            SetFailure(encoder, "Unity returned no Metal device.");
            *handle = encoder;
            return -6;
        }
        if (!InitializeConversionPipeline(encoder, device))
        {
            *handle = encoder;
            return -7;
        }

        cvStatus = CVMetalTextureCacheCreate(
            kCFAllocatorDefault,
            nullptr,
            device,
            nullptr,
            &encoder->metalTextureCache);
        if (cvStatus != kCVReturnSuccess || encoder->metalTextureCache == nullptr)
        {
            SetFailure(encoder, StatusMessage("CVMetalTextureCacheCreate", cvStatus));
            *handle = encoder;
            return -7;
        }

        encoder->slots.resize(kSlotCount);
        for (int i = 0; i < kSlotCount; i++)
        {
            auto &slot = encoder->slots[(size_t)i];
            cvStatus = CVPixelBufferPoolCreatePixelBuffer(
                kCFAllocatorDefault,
                encoder->pixelBufferPool,
                &slot.pixelBuffer);
            if (cvStatus != kCVReturnSuccess || slot.pixelBuffer == nullptr)
            {
                SetFailure(encoder, StatusMessage("CVPixelBufferPoolCreatePixelBuffer", cvStatus));
                *handle = encoder;
                return -8;
            }

            CVBufferSetAttachment(
                slot.pixelBuffer,
                kCVImageBufferYCbCrMatrixKey,
                kCVImageBufferYCbCrMatrix_ITU_R_709_2,
                kCVAttachmentMode_ShouldPropagate);
            CVBufferSetAttachment(
                slot.pixelBuffer,
                kCVImageBufferColorPrimariesKey,
                kCVImageBufferColorPrimaries_ITU_R_709_2,
                kCVAttachmentMode_ShouldPropagate);
            CVBufferSetAttachment(
                slot.pixelBuffer,
                kCVImageBufferTransferFunctionKey,
                kCVImageBufferTransferFunction_ITU_R_709_2,
                kCVAttachmentMode_ShouldPropagate);

            if (CVPixelBufferGetPlaneCount(slot.pixelBuffer) != 2)
            {
                SetFailure(encoder, "VideoToolbox NV12 pixel buffer did not expose two planes.");
                *handle = encoder;
                return -9;
            }

            cvStatus = CVMetalTextureCacheCreateTextureFromImage(
                kCFAllocatorDefault,
                encoder->metalTextureCache,
                slot.pixelBuffer,
                nullptr,
                MTLPixelFormatR8Unorm,
                width,
                height,
                0,
                &slot.lumaTexture);
            if (cvStatus != kCVReturnSuccess || slot.lumaTexture == nullptr)
            {
                SetFailure(encoder, StatusMessage("CVMetalTextureCacheCreateTextureFromImage(Y)", cvStatus));
                *handle = encoder;
                return -10;
            }

            cvStatus = CVMetalTextureCacheCreateTextureFromImage(
                kCFAllocatorDefault,
                encoder->metalTextureCache,
                slot.pixelBuffer,
                nullptr,
                MTLPixelFormatRG8Unorm,
                width / 2,
                height / 2,
                1,
                &slot.chromaTexture);
            if (cvStatus != kCVReturnSuccess || slot.chromaTexture == nullptr)
            {
                SetFailure(encoder, StatusMessage("CVMetalTextureCacheCreateTextureFromImage(UV)", cvStatus));
                *handle = encoder;
                return -11;
            }
        }

        OSStatus status = VTCompressionSessionCreate(
            kCFAllocatorDefault,
            width,
            height,
            kCMVideoCodecType_H264,
            nullptr,
            (__bridge CFDictionaryRef)pixelBufferAttributes,
            nullptr,
            CompressionOutputCallback,
            encoder,
            &encoder->compressionSession);
        if (status != noErr || encoder->compressionSession == nullptr)
        {
            SetFailure(encoder, StatusMessage("VTCompressionSessionCreate", status));
            *handle = encoder;
            return -10;
        }

        int expectedFrameRate = fps;
        int maxFrameDelayCount = 2;
        int keyFrameInterval = fps * 2;
        int averageBitrate = bitrateBitsPerSecond;
        CFNumberRef expectedFrameRateNumber = CFNumberCreate(
            kCFAllocatorDefault, kCFNumberIntType, &expectedFrameRate);
        CFNumberRef maxFrameDelayCountNumber = CFNumberCreate(
            kCFAllocatorDefault, kCFNumberIntType, &maxFrameDelayCount);
        CFNumberRef keyFrameIntervalNumber = CFNumberCreate(
            kCFAllocatorDefault, kCFNumberIntType, &keyFrameInterval);
        CFNumberRef bitrateNumber = CFNumberCreate(
            kCFAllocatorDefault, kCFNumberIntType, &averageBitrate);

        bool configured =
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_RealTime, kCFBooleanTrue, "VideoToolbox realtime mode") &&
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_AllowFrameReordering, kCFBooleanFalse, "VideoToolbox frame reordering") &&
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_ExpectedFrameRate, expectedFrameRateNumber, "VideoToolbox expected frame rate") &&
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_MaxKeyFrameInterval, keyFrameIntervalNumber, "VideoToolbox keyframe interval") &&
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_AverageBitRate, bitrateNumber, "VideoToolbox average bitrate") &&
            SetCompressionProperty(
                encoder, kVTCompressionPropertyKey_ProfileLevel, kVTProfileLevel_H264_High_AutoLevel, "VideoToolbox H.264 profile");

        if (configured)
        {
            // Bounding VideoToolbox's compression window is what guarantees that the
            // IOSurface slots return to Unity. This is best-effort because the managed
            // path still degrades honestly on encoders that reject the optional key.
            VTSessionSetProperty(
                encoder->compressionSession,
                kVTCompressionPropertyKey_MaxFrameDelayCount,
                maxFrameDelayCountNumber);
            configured =
                SetCompressionProperty(
                    encoder,
                    kVTCompressionPropertyKey_ColorPrimaries,
                    kCVImageBufferColorPrimaries_ITU_R_709_2,
                    "VideoToolbox BT.709 color primaries") &&
                SetCompressionProperty(
                    encoder,
                    kVTCompressionPropertyKey_TransferFunction,
                    kCVImageBufferTransferFunction_ITU_R_709_2,
                    "VideoToolbox BT.709 transfer function") &&
                SetCompressionProperty(
                    encoder,
                    kVTCompressionPropertyKey_YCbCrMatrix,
                    kCVImageBufferYCbCrMatrix_ITU_R_709_2,
                    "VideoToolbox BT.709 YCbCr matrix");
        }

        CFRelease(expectedFrameRateNumber);
        CFRelease(maxFrameDelayCountNumber);
        CFRelease(keyFrameIntervalNumber);
        CFRelease(bitrateNumber);

        if (!configured)
        {
            *handle = encoder;
            return -11;
        }

        status = VTCompressionSessionPrepareToEncodeFrames(encoder->compressionSession);
        if (status != noErr)
        {
            SetFailure(encoder, StatusMessage("VTCompressionSessionPrepareToEncodeFrames", status));
            *handle = encoder;
            return -12;
        }

        *handle = encoder;
        return 0;
    }
}

__attribute__((visibility("default"))) int BppVtGetSlotCount(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    return encoder == nullptr ? 0 : (int)encoder->slots.size();
}

__attribute__((visibility("default"))) int BppVtAcquireSlot(void *handle, int *slotIndex)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr || slotIndex == nullptr)
        return 0;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (encoder->failed || !encoder->acceptingFrames || encoder->finished)
        return 0;
    for (int i = 0; i < (int)encoder->slots.size(); i++)
    {
        if (encoder->slots[(size_t)i].state != SlotState::Free)
            continue;
        encoder->slots[(size_t)i].state = SlotState::GpuPending;
        *slotIndex = i;
        return 1;
    }

    encoder->stats.acquireMisses++;
    return 0;
}

__attribute__((visibility("default"))) void *BppVtPrepareRenderEvent(
    void *handle,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (
        encoder == nullptr || slotIndex < 0 || slotIndex >= (int)encoder->slots.size() ||
        frameCount <= 0)
        return nullptr;

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        if (
            encoder->failed || !encoder->acceptingFrames || encoder->finished ||
            encoder->slots[(size_t)slotIndex].state != SlotState::GpuPending)
        {
            return nullptr;
        }
        encoder->renderEventsPending++;
    }

    return new RenderEventPacket{encoder, slotIndex, firstFrameIndex, frameCount};
}

__attribute__((visibility("default"))) void BppVtCommitRenderEvent(void *data)
{
    ReleaseRenderEventPacket(static_cast<RenderEventPacket *>(data));
}

__attribute__((visibility("default"))) void BppVtCancelRenderEvent(void *data)
{
    auto *packet = static_cast<RenderEventPacket *>(data);
    if (packet == nullptr)
        return;

    uint8_t expectedState = 0;
    if (packet->state.compare_exchange_strong(
            expectedState,
            2,
            std::memory_order_acq_rel))
    {
        ReleaseGpuPendingSlot(packet->encoder, packet->slotIndex);
        CompleteRenderEvent(packet->encoder);
    }
    ReleaseRenderEventPacket(packet);
}

__attribute__((visibility("default"))) void BppVtDiscardRenderEvent(void *data)
{
    auto *packet = static_cast<RenderEventPacket *>(data);
    if (packet == nullptr)
        return;

    uint8_t expectedState = 0;
    if (packet->state.compare_exchange_strong(
            expectedState,
            2,
            std::memory_order_acq_rel))
    {
        ReleaseGpuPendingSlot(packet->encoder, packet->slotIndex);
        CompleteRenderEvent(packet->encoder);
        ReleaseRenderEventPacket(packet);
    }
    ReleaseRenderEventPacket(packet);
}

__attribute__((visibility("default"))) void BppVtReleaseSlot(void *handle, int slotIndex)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    if (slotIndex >= 0 && slotIndex < (int)encoder->slots.size())
    {
        auto &slot = encoder->slots[(size_t)slotIndex];
        if (slot.state == SlotState::GpuPending)
            slot.state = SlotState::Free;
    }
}

__attribute__((visibility("default"))) int BppVtFinish(void *handle, int timeoutMs)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return 0;

    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::milliseconds(std::max(1, timeoutMs));
    bool renderEventsDrained = false;
    {
        std::unique_lock<std::mutex> guard(encoder->mutex);
        if (encoder->finished)
            return encoder->failed ? 0 : 1;
        encoder->acceptingFrames = false;
        renderEventsDrained = encoder->renderEventCondition.wait_until(
            guard,
            deadline,
            [encoder] { return encoder->renderEventsPending == 0; });
    }

    if (!renderEventsDrained)
    {
        SetFailure(encoder, "Timed out waiting for Unity Metal render events to finish.");
        CancelWriterOnQueue(encoder);
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->finished = true;
        return 0;
    }

    OSStatus status = VTCompressionSessionCompleteFrames(
        encoder->compressionSession,
        kCMTimeInvalid);
    if (status != noErr)
        SetFailure(encoder, StatusMessage("VTCompressionSessionCompleteFrames", status));

    bool writerCallbacksDrained = false;
    {
        std::unique_lock<std::mutex> guard(encoder->mutex);
        writerCallbacksDrained = encoder->writerCondition.wait_until(
            guard,
            deadline,
            [encoder] {
                return encoder->failed ||
                    encoder->stats.appendedFrames == encoder->stats.submittedFrames;
            });
    }
    if (!writerCallbacksDrained)
        SetFailure(encoder, "Timed out waiting for VideoToolbox output callbacks to append every frame.");

    dispatch_sync(encoder->writerQueue, ^{});

    if (!encoder->writerStarted)
    {
        SetFailure(encoder, "VideoToolbox completed without producing an encoded frame.");
    }
    else
    {
        dispatch_semaphore_t completion = dispatch_semaphore_create(0);
        dispatch_sync(encoder->writerQueue, ^{
          @autoreleasepool
          {
              encoder->writerClosed = true;
              @try
              {
                  [encoder->writerInput markAsFinished];
                  [encoder->writer finishWritingWithCompletionHandler:^{
                    dispatch_semaphore_signal(completion);
                  }];
              }
              @catch (NSException *exception)
              {
                  SetFailure(
                      encoder,
                      exception.reason == nil
                          ? "AVAssetWriter threw while finishing."
                          : std::string(exception.reason.UTF8String));
                  dispatch_semaphore_signal(completion);
              }
          }
        });

        const auto remaining = std::chrono::duration_cast<std::chrono::milliseconds>(
            deadline - std::chrono::steady_clock::now()).count();
        if (
            remaining <= 0 ||
            dispatch_semaphore_wait(
                completion,
                dispatch_time(DISPATCH_TIME_NOW, remaining * NSEC_PER_MSEC)) != 0)
        {
            SetFailure(encoder, "AVAssetWriter timed out while finishing the MP4.");
            CancelWriterOnQueue(encoder);
        }
        else if (encoder->writer.status != AVAssetWriterStatusCompleted)
        {
            NSString *message = encoder->writer.error.localizedDescription;
            SetFailure(
                encoder,
                message == nil ? "AVAssetWriter did not complete the MP4."
                               : std::string(message.UTF8String));
        }
    }

    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->finished = true;
        return encoder->failed ? 0 : 1;
    }
}

__attribute__((visibility("default"))) void BppVtDestroy(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return;

    bool needsFinish = false;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        encoder->acceptingFrames = false;
        needsFinish =
            encoder->compressionSession != nullptr && !encoder->finished &&
            (encoder->stats.submittedFrames > 0 || encoder->renderEventsPending > 0);
    }
    if (needsFinish)
        BppVtFinish(handle, 2000);

    bool renderEventsPending = false;
    {
        std::lock_guard<std::mutex> guard(encoder->mutex);
        renderEventsPending = encoder->renderEventsPending > 0;
        needsFinish =
            encoder->compressionSession != nullptr && !encoder->finished &&
            encoder->stats.submittedFrames > 0;
    }

    // A timed-out Unity command buffer still owns a callback with this raw encoder pointer.
    // Leaking the failed encoder is safer than freeing it underneath Metal; process teardown will
    // reclaim it, and the normal completion path always reaches zero pending events.
    if (renderEventsPending)
        return;

    if (needsFinish)
        BppVtFinish(handle, 2000);

    if (encoder->encodeQueue != nullptr)
        dispatch_sync(encoder->encodeQueue, ^{});
    if (encoder->writerQueue != nullptr)
        dispatch_sync(encoder->writerQueue, ^{});

    DestroyEncoder(encoder);
    delete encoder;
}

__attribute__((visibility("default"))) int BppVtIsFailed(void *handle)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (encoder == nullptr)
        return 1;
    std::lock_guard<std::mutex> guard(encoder->mutex);
    return encoder->failed ? 1 : 0;
}

__attribute__((visibility("default"))) void BppVtGetStats(
    void *handle,
    BppVtNativeStats *stats)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (stats == nullptr)
        return;
    std::memset(stats, 0, sizeof(BppVtNativeStats));
    if (encoder == nullptr)
        return;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    *stats = encoder->stats;
}

__attribute__((visibility("default"))) int BppVtCopyError(
    void *handle,
    char *buffer,
    int capacity)
{
    auto *encoder = static_cast<Encoder *>(handle);
    if (buffer == nullptr || capacity <= 0)
        return 0;
    buffer[0] = '\0';
    if (encoder == nullptr)
        return 0;

    std::lock_guard<std::mutex> guard(encoder->mutex);
    const int copied = std::min((int)encoder->error.size(), capacity - 1);
    if (copied > 0)
        std::memcpy(buffer, encoder->error.data(), (size_t)copied);
    buffer[copied] = '\0';
    return copied;
}
}
