#import <AVFoundation/AVFoundation.h>

#include "BppReplayVideoToolbox.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <thread>
#include <vector>

namespace
{
void CopyError(NSString *message, char *buffer, int capacity)
{
    if (buffer == nullptr || capacity <= 0)
        return;

    buffer[0] = '\0';
    const char *utf8 = message.UTF8String;
    if (utf8 == nullptr)
        return;

    const int copied = std::min((int)std::strlen(utf8), capacity - 1);
    if (copied > 0)
        std::memcpy(buffer, utf8, (size_t)copied);
    buffer[copied] = '\0';
}

int Fail(int code, NSString *message, char *buffer, int capacity)
{
    CopyError(message ?: @"Unknown AVFoundation mux failure.", buffer, capacity);
    return code;
}

NSString *ErrorMessage(NSString *prefix, NSError *error)
{
    NSString *detail = error.localizedDescription;
    return detail.length == 0 ? prefix : [NSString stringWithFormat:@"%@: %@", prefix, detail];
}

bool IsReaderFailure(AVAssetReader *reader)
{
    return reader.status == AVAssetReaderStatusFailed || reader.status == AVAssetReaderStatusCancelled;
}

void ReleaseSample(CMSampleBufferRef &sample)
{
    if (sample == nullptr)
        return;
    CFRelease(sample);
    sample = nullptr;
}
} // namespace

extern "C"
{
__attribute__((visibility("default"))) int BppVtCanMuxAudio()
{
    return 1;
}

// Copies the already-compressed H.264 samples without re-encoding and converts the captured PCM
// WAV tracks to stereo 48 kHz AAC. The caller runs this on its finalize worker, never on Unity's
// render thread. Return value 1 means success; negative values are stable failure categories.
__attribute__((visibility("default"))) int BppVtMuxAudio(
    const char *silentVideoPath,
    const char *const *wavPaths,
    int wavPathCount,
    const char *finalPath,
    int audioBitrateBitsPerSecond,
    int timeoutMs,
    char *errorBuffer,
    int errorCapacity)
{
    if (errorBuffer != nullptr && errorCapacity > 0)
        errorBuffer[0] = '\0';
    if (
        silentVideoPath == nullptr || wavPaths == nullptr || wavPathCount <= 0 ||
        finalPath == nullptr || audioBitrateBitsPerSecond <= 0 || timeoutMs <= 0)
    {
        return Fail(-1, @"Invalid native audio mux arguments.", errorBuffer, errorCapacity);
    }

    @autoreleasepool
    {
        @try
        {
            NSString *videoPath = [NSString stringWithUTF8String:silentVideoPath];
            NSString *outputPath = [NSString stringWithUTF8String:finalPath];
            if (videoPath.length == 0 || outputPath.length == 0)
                return Fail(-1, @"A mux path is not valid UTF-8.", errorBuffer, errorCapacity);

            NSFileManager *files = NSFileManager.defaultManager;
            if (![files fileExistsAtPath:videoPath])
                return Fail(-2, @"The silent video input does not exist.", errorBuffer, errorCapacity);

            AVURLAsset *videoAsset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:videoPath]
                                                         options:nil];
            NSArray<AVAssetTrack *> *videoTracks = [videoAsset tracksWithMediaType:AVMediaTypeVideo];
            AVAssetTrack *videoTrack = videoTracks.firstObject;
            if (
                videoTrack == nil || CMTIME_IS_INVALID(videoAsset.duration) ||
                CMTimeCompare(videoAsset.duration, kCMTimeZero) <= 0)
                return Fail(-2, @"The silent video has no usable video track.", errorBuffer, errorCapacity);

            AVMutableComposition *audioComposition = [AVMutableComposition composition];
            NSMutableArray<AVMutableCompositionTrack *> *compositionAudioTracks =
                [NSMutableArray arrayWithCapacity:(NSUInteger)wavPathCount];
            for (int index = 0; index < wavPathCount; index++)
            {
                const char *rawPath = wavPaths[index];
                NSString *wavPath = rawPath == nullptr ? nil : [NSString stringWithUTF8String:rawPath];
                if (wavPath.length == 0 || ![files fileExistsAtPath:wavPath])
                    return Fail(-2, @"A captured WAV input does not exist.", errorBuffer, errorCapacity);

                AVURLAsset *audioAsset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:wavPath]
                                                             options:nil];
                AVAssetTrack *sourceTrack = [audioAsset tracksWithMediaType:AVMediaTypeAudio].firstObject;
                if (
                    sourceTrack == nil || CMTIME_IS_INVALID(audioAsset.duration) ||
                    CMTimeCompare(audioAsset.duration, kCMTimeZero) <= 0)
                    return Fail(-2, @"A captured WAV has no usable audio track.", errorBuffer, errorCapacity);

                AVMutableCompositionTrack *compositionTrack =
                    [audioComposition addMutableTrackWithMediaType:AVMediaTypeAudio
                                                   preferredTrackID:kCMPersistentTrackID_Invalid];
                if (compositionTrack == nil)
                    return Fail(-3, @"AVFoundation could not create an audio composition track.", errorBuffer, errorCapacity);

                NSError *insertError = nil;
                if (![compositionTrack insertTimeRange:CMTimeRangeMake(kCMTimeZero, audioAsset.duration)
                                                ofTrack:sourceTrack
                                                 atTime:kCMTimeZero
                                                  error:&insertError])
                {
                    return Fail(
                        -3,
                        ErrorMessage(@"AVFoundation could not add a WAV to the audio mix", insertError),
                        errorBuffer,
                        errorCapacity);
                }
                [compositionAudioTracks addObject:compositionTrack];
            }

            NSError *readerError = nil;
            AVAssetReader *videoReader = [[AVAssetReader alloc] initWithAsset:videoAsset error:&readerError];
            if (videoReader == nil)
                return Fail(-3, ErrorMessage(@"Could not create the video reader", readerError), errorBuffer, errorCapacity);

            AVAssetReaderTrackOutput *videoOutput =
                [[AVAssetReaderTrackOutput alloc] initWithTrack:videoTrack outputSettings:nil];
            videoOutput.alwaysCopiesSampleData = NO;
            if (![videoReader canAddOutput:videoOutput])
                return Fail(-3, @"AVAssetReader rejected the H.264 passthrough output.", errorBuffer, errorCapacity);
            [videoReader addOutput:videoOutput];

            readerError = nil;
            AVAssetReader *audioReader =
                [[AVAssetReader alloc] initWithAsset:audioComposition error:&readerError];
            if (audioReader == nil)
                return Fail(-3, ErrorMessage(@"Could not create the audio reader", readerError), errorBuffer, errorCapacity);

            NSDictionary *pcmSettings = @{
                AVFormatIDKey : @(kAudioFormatLinearPCM),
                AVSampleRateKey : @48000,
                AVNumberOfChannelsKey : @2,
                AVLinearPCMBitDepthKey : @32,
                AVLinearPCMIsFloatKey : @YES,
                AVLinearPCMIsBigEndianKey : @NO,
                AVLinearPCMIsNonInterleaved : @NO,
            };
            AVAssetReaderAudioMixOutput *audioOutput =
                [[AVAssetReaderAudioMixOutput alloc] initWithAudioTracks:compositionAudioTracks
                                                           audioSettings:pcmSettings];
            audioOutput.alwaysCopiesSampleData = NO;

            NSMutableArray<AVMutableAudioMixInputParameters *> *mixParameters =
                [NSMutableArray arrayWithCapacity:compositionAudioTracks.count];
            for (AVMutableCompositionTrack *track in compositionAudioTracks)
            {
                AVMutableAudioMixInputParameters *parameters =
                    [AVMutableAudioMixInputParameters audioMixInputParametersWithTrack:track];
                [parameters setVolume:1.0f atTime:kCMTimeZero];
                [mixParameters addObject:parameters];
            }
            AVMutableAudioMix *audioMix = [AVMutableAudioMix audioMix];
            audioMix.inputParameters = mixParameters;
            audioOutput.audioMix = audioMix;

            if (![audioReader canAddOutput:audioOutput])
                return Fail(-3, @"AVAssetReader rejected the PCM audio mix output.", errorBuffer, errorCapacity);
            [audioReader addOutput:audioOutput];

            NSURL *outputUrl = [NSURL fileURLWithPath:outputPath];
            [files removeItemAtURL:outputUrl error:nil];
            NSError *writerError = nil;
            AVAssetWriter *writer = [[AVAssetWriter alloc] initWithURL:outputUrl
                                                              fileType:AVFileTypeMPEG4
                                                                 error:&writerError];
            if (writer == nil)
                return Fail(-3, ErrorMessage(@"Could not create the MP4 writer", writerError), errorBuffer, errorCapacity);
            writer.shouldOptimizeForNetworkUse = YES;

            CMFormatDescriptionRef videoFormat =
                (__bridge CMFormatDescriptionRef)videoTrack.formatDescriptions.firstObject;
            if (videoFormat == nullptr)
                return Fail(-2, @"The H.264 input has no format description.", errorBuffer, errorCapacity);

            AVAssetWriterInput *videoInput =
                [[AVAssetWriterInput alloc] initWithMediaType:AVMediaTypeVideo
                                              outputSettings:nil
                                            sourceFormatHint:videoFormat];
            videoInput.expectsMediaDataInRealTime = NO;

            NSDictionary *aacSettings = @{
                AVFormatIDKey : @(kAudioFormatMPEG4AAC),
                AVSampleRateKey : @48000,
                AVNumberOfChannelsKey : @2,
                AVEncoderBitRateKey : @(audioBitrateBitsPerSecond),
            };
            AVAssetWriterInput *audioInput =
                [[AVAssetWriterInput alloc] initWithMediaType:AVMediaTypeAudio
                                              outputSettings:aacSettings];
            audioInput.expectsMediaDataInRealTime = NO;

            if (![writer canAddInput:videoInput] || ![writer canAddInput:audioInput])
                return Fail(-3, @"AVAssetWriter rejected the video or AAC input.", errorBuffer, errorCapacity);
            [writer addInput:videoInput];
            [writer addInput:audioInput];

            if (![videoReader startReading] || ![audioReader startReading] || ![writer startWriting])
            {
                NSError *startError = writer.error ?: videoReader.error ?: audioReader.error;
                return Fail(-4, ErrorMessage(@"AVFoundation could not start the native mux", startError), errorBuffer, errorCapacity);
            }
            [writer startSessionAtSourceTime:kCMTimeZero];

            const auto deadline = std::chrono::steady_clock::now() +
                std::chrono::milliseconds(std::max(1, timeoutMs));
            CMSampleBufferRef pendingVideo = nullptr;
            CMSampleBufferRef pendingAudio = nullptr;
            bool videoFinished = false;
            bool audioFinished = false;
            int appendedAudioSamples = 0;

            while (!videoFinished || !audioFinished)
            {
                if (std::chrono::steady_clock::now() >= deadline)
                {
                    ReleaseSample(pendingVideo);
                    ReleaseSample(pendingAudio);
                    [videoReader cancelReading];
                    [audioReader cancelReading];
                    [writer cancelWriting];
                    return Fail(-8, @"AVFoundation timed out while muxing audio.", errorBuffer, errorCapacity);
                }

                bool progressed = false;
                if (!videoFinished && pendingVideo == nullptr)
                {
                    pendingVideo = [videoOutput copyNextSampleBuffer];
                    if (pendingVideo == nullptr && videoReader.status == AVAssetReaderStatusCompleted)
                    {
                        [videoInput markAsFinished];
                        videoFinished = true;
                        progressed = true;
                    }
                }
                if (!audioFinished && pendingAudio == nullptr)
                {
                    pendingAudio = [audioOutput copyNextSampleBuffer];
                    if (pendingAudio == nullptr && audioReader.status == AVAssetReaderStatusCompleted)
                    {
                        [audioInput markAsFinished];
                        audioFinished = true;
                        progressed = true;
                    }
                }

                if (IsReaderFailure(videoReader) || IsReaderFailure(audioReader))
                {
                    NSError *failure = videoReader.error ?: audioReader.error;
                    ReleaseSample(pendingVideo);
                    ReleaseSample(pendingAudio);
                    [writer cancelWriting];
                    return Fail(-6, ErrorMessage(@"AVAssetReader failed during the native mux", failure), errorBuffer, errorCapacity);
                }

                if (pendingVideo != nullptr && videoInput.readyForMoreMediaData)
                {
                    if (![videoInput appendSampleBuffer:pendingVideo])
                    {
                        ReleaseSample(pendingVideo);
                        ReleaseSample(pendingAudio);
                        [writer cancelWriting];
                        return Fail(-5, ErrorMessage(@"Could not append an H.264 sample", writer.error), errorBuffer, errorCapacity);
                    }
                    ReleaseSample(pendingVideo);
                    progressed = true;
                }

                if (pendingAudio != nullptr && audioInput.readyForMoreMediaData)
                {
                    CMTime presentationTime = CMSampleBufferGetPresentationTimeStamp(pendingAudio);
                    if (CMTIME_IS_VALID(presentationTime) && CMTimeCompare(presentationTime, videoAsset.duration) >= 0)
                    {
                        ReleaseSample(pendingAudio);
                        [audioInput markAsFinished];
                        audioFinished = true;
                    }
                    else
                    {
                        if (![audioInput appendSampleBuffer:pendingAudio])
                        {
                            ReleaseSample(pendingVideo);
                            ReleaseSample(pendingAudio);
                            [writer cancelWriting];
                            return Fail(-5, ErrorMessage(@"Could not append an AAC source sample", writer.error), errorBuffer, errorCapacity);
                        }
                        appendedAudioSamples++;
                        ReleaseSample(pendingAudio);
                    }
                    progressed = true;
                }

                if (!progressed)
                    std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }

            if (appendedAudioSamples == 0)
            {
                [writer cancelWriting];
                return Fail(-7, @"The captured WAV contained no audio samples.", errorBuffer, errorCapacity);
            }

            dispatch_semaphore_t finished = dispatch_semaphore_create(0);
            [writer finishWritingWithCompletionHandler:^{
              dispatch_semaphore_signal(finished);
            }];

            auto remaining = std::chrono::duration_cast<std::chrono::milliseconds>(
                deadline - std::chrono::steady_clock::now()).count();
            if (
                remaining <= 0 ||
                dispatch_semaphore_wait(
                    finished,
                    dispatch_time(DISPATCH_TIME_NOW, remaining * NSEC_PER_MSEC)) != 0)
            {
                [writer cancelWriting];
                return Fail(-8, @"AVAssetWriter timed out while finalizing the MP4.", errorBuffer, errorCapacity);
            }

            if (writer.status != AVAssetWriterStatusCompleted)
                return Fail(-9, ErrorMessage(@"AVAssetWriter did not complete the MP4", writer.error), errorBuffer, errorCapacity);

            return 1;
        }
        @catch (NSException *exception)
        {
            return Fail(-10, exception.reason ?: exception.name, errorBuffer, errorCapacity);
        }
    }
}
}
