#import <UIKit/UIKit.h>
#import <UnityFramework/UnityFramework.h>
#import <dlfcn.h>
#import <mach-o/ldsyms.h>
#import <mach/mach.h>
#if BPP_STEAM_TEST_CONTROL && TARGET_OS_SIMULATOR
#include <sys/socket.h>
#include <sys/un.h>
#include <sys/stat.h>
#include <unistd.h>
#endif
static UnityFramework *engine;
static int (*startService)(const char *);
static void (*sendCommand)(const char *);
static char *(*snapshotService)(void);
static void (*freeService)(void *);
static int savedArgc; static char **savedArgv;
static NSString *gameRoot;
static NSString *jsonString(NSDictionary *value) {
    return [[NSString alloc] initWithData:[NSJSONSerialization dataWithJSONObject:value options:0 error:nil] encoding:NSUTF8StringEncoding];
}
// Unity's Application.version reads CFBundleShortVersionString, which holds the numeric Product Release
// App Store Connect requires; the game and the mod need the game's own version string there. The main
// bundle's info dictionary is mutable storage shared by NSBundle and CFBundle readers.
static void exposeGameVersion(void) {
    NSString *game = [NSBundle.mainBundle objectForInfoDictionaryKey:@"BPPGameVersion"];
    @try { [(NSMutableDictionary *)NSBundle.mainBundle.infoDictionary setObject:game forKey:@"CFBundleShortVersionString"]; }
    @catch (NSException *error) { fprintf(stderr,"STEAM_APP_GAME_VERSION failed=%s\n",error.reason.UTF8String); return; }
    fprintf(stderr,"STEAM_APP_GAME_VERSION %s\n",[[NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"] UTF8String]);
}
// The launcher UI is SwiftUI (app/); this controller keeps the engine,
// the service bridge, the refresh timer and the game hand-off.
@protocol BPPLauncherHosting <NSObject>
- (instancetype)initWithCommand:(void (^)(NSDictionary *))command launch:(void (^)(void))launch;
- (UIViewController *)viewController;
- (void)update:(NSDictionary *)snapshot;
- (void)fail:(NSString *)message;
- (void)releaseForGame;
@end
@interface SteamController : UIViewController
@property(nonatomic, strong) NSTimer *timer;
@property(nonatomic, strong) id<BPPLauncherHosting> host;
@property(nonatomic) BOOL launched;
@end
@implementation SteamController
- (void)viewDidLoad {
    [super viewDidLoad]; self.overrideUserInterfaceStyle=UIUserInterfaceStyleDark; self.view.backgroundColor=UIColor.blackColor;
    __weak SteamController *weakSelf=self;
    self.host=[[(Class)NSClassFromString(@"BPPLauncherHost") alloc] initWithCommand:^(NSDictionary *value){ [weakSelf command:value]; }
                                                                        launch:^{ [weakSelf playTapped]; }];
    UIViewController *ui=self.host.viewController; [self addChildViewController:ui];
    ui.view.frame=self.view.bounds; ui.view.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;
    [self.view addSubview:ui.view]; [ui didMoveToParentViewController:self];
    NSString *path=[NSBundle.mainBundle.bundlePath stringByAppendingPathComponent:@"Frameworks/UnityFramework.framework"];
    NSBundle *bundle=[NSBundle bundleWithPath:path]; [bundle load]; engine=[bundle.principalClass getInstance]; [engine setExecuteHeader:&_mh_execute_header];
    startService=(int(*)(const char*))dlsym(RTLD_DEFAULT,"bpp_steam_start");
    sendCommand=(void(*)(const char*))dlsym(RTLD_DEFAULT,"bpp_steam_command");
    snapshotService=(char*(*)(void))dlsym(RTLD_DEFAULT,"bpp_steam_snapshot"); freeService=(void(*)(void*))dlsym(RTLD_DEFAULT,"bpp_steam_free");
    gameRoot=[NSSearchPathForDirectoriesInDomains(NSApplicationSupportDirectory,NSUserDomainMask,YES).firstObject stringByAppendingPathComponent:@"Games"];
    [NSFileManager.defaultManager createDirectoryAtPath:gameRoot withIntermediateDirectories:YES attributes:nil error:nil];
    [[NSURL fileURLWithPath:gameRoot] setResourceValue:@YES forKey:NSURLIsExcludedFromBackupKey error:nil];
    // Steam shows this name on the phone when asking the player to approve a login.
    NSString *appName=[NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleDisplayName"] ?: @"Launcher";
    NSString *deviceName=[NSString stringWithFormat:@"%@ · %@%@",appName,UIDevice.currentDevice.model,TARGET_OS_SIMULATOR ? @"（模拟器）" : @""];
    NSString *config=jsonString(@{@"root":gameRoot,@"assets":NSBundle.mainBundle.bundlePath,@"platform":TARGET_OS_SIMULATOR ? @"simulator" : @"device",@"deviceName":deviceName});
    if(!startService || !sendCommand || !snapshotService || !freeService || !startService(config.UTF8String)) { [self.host fail:@"运行时初始化失败"]; return; }
    self.timer=[NSTimer scheduledTimerWithTimeInterval:0.5 target:self selector:@selector(refresh) userInfo:nil repeats:YES]; [self refresh];
#if BPP_STEAM_TEST_CONTROL && TARGET_OS_SIMULATOR
    [self startTestControl];
#endif
}
- (UIViewController *)childViewControllerForStatusBarStyle { return self.host.viewController; }
- (void)didReceiveMemoryWarning {
    [super didReceiveMemoryWarning]; fprintf(stderr,"STEAM_MEMORY_WARNING\n");
}
#if BPP_STEAM_TEST_CONTROL && TARGET_OS_SIMULATOR
// Explicit opt-in integration harness. Never built for devices or normal app builds.
- (void)startTestControl {
    const char *path = getenv("BPP_STEAM_TEST_SOCKET");
    if (!path || strncmp(path,"/tmp/bpp-steam-",14) || strlen(path) >= sizeof(((struct sockaddr_un*)0)->sun_path)) return;
    int server = socket(AF_UNIX,SOCK_STREAM,0);
    struct sockaddr_un address = {0}; address.sun_family=AF_UNIX; strlcpy(address.sun_path,path,sizeof(address.sun_path));
    mode_t old = umask(0077); int bound=bind(server,(struct sockaddr*)&address,sizeof(address)); umask(old);
    if(bound || listen(server,4)) {close(server);return;}
    fprintf(stderr,"STEAM_TEST_CONTROL_READY\n");
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
        for (;;) {
            int peer=accept(server,NULL,NULL); if(peer<0)break;
            int noSignal=1; setsockopt(peer,SOL_SOCKET,SO_NOSIGPIPE,&noSignal,sizeof(noSignal));
            @autoreleasepool {
                char input[16384]={0}; size_t size=0;
                while(size<sizeof(input)-1) {ssize_t n=read(peer,input+size,1);if(n!=1||input[size]=='\n')break;size++;}
                NSData *data=[NSData dataWithBytes:input length:size];
                NSDictionary *command=[NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
                if (![command isKindOfClass:NSDictionary.class]) { close(peer); continue; }
                __block NSString *reply=@"{}";
                dispatch_sync(dispatch_get_main_queue(), ^{
                    NSString *action=command[@"action"];
                    if ([action hasPrefix:@"audio-"]) {
                        if ([action isEqualToString:@"audio-background"])
                            [NSNotificationCenter.defaultCenter postNotificationName:UIApplicationDidEnterBackgroundNotification object:nil];
                        if ([action isEqualToString:@"audio-foreground"])
                            [NSNotificationCenter.defaultCenter postNotificationName:UIApplicationDidBecomeActiveNotification object:nil];
                        const char *(*audioSnapshot)(void)=(const char *(*)(void))dlsym(RTLD_DEFAULT,"bpp_fmod_snapshot");
                        if(audioSnapshot) reply=[NSString stringWithUTF8String:audioSnapshot()];
                        return;
                    }
                    if([action isEqualToString:@"launch"]){[self playTapped];}
                    else if(action && ![action isEqualToString:@"snapshot"]){[self command:command];}
                    char *text=snapshotService();if(text){reply=[NSString stringWithUTF8String:text];freeService(text);}
                });
                memset(input,0,sizeof(input));
                NSData *out=[reply dataUsingEncoding:NSUTF8StringEncoding];write(peer,out.bytes,out.length);
            }
            close(peer);
        }
        close(server);
    });
}
#endif
- (void)command:(NSDictionary*)value { if(sendCommand) sendCommand(jsonString(value).UTF8String); }
- (NSDictionary*)readSnapshot {
    char *text=snapshotService ? snapshotService() : NULL; if(!text) return @{};
    NSData *data=[NSData dataWithBytes:text length:strlen(text)]; freeService(text);
    return [NSJSONSerialization JSONObjectWithData:data options:0 error:nil] ?: @{};
}
- (void)refresh {
    NSDictionary *s=[self readSnapshot]; if(!s.count) return;
    BOOL busy=[s[@"busy"] boolValue];
    UIApplication.sharedApplication.idleTimerDisabled=busy;
    if (getenv("BPP_DOWNLOAD_DIAGNOSTICS")) {
        static NSTimeInterval last=0; NSTimeInterval now=NSProcessInfo.processInfo.systemUptime;
        if (now-last>=5) {
            last=now; task_vm_info_data_t vm={}; mach_msg_type_number_t count=TASK_VM_INFO_COUNT;
            kern_return_t result=task_info(mach_task_self(),TASK_VM_INFO,(task_info_t)&vm,&count);
            auto heap=(int64_t(*)(void))dlsym(RTLD_DEFAULT,"mono_gc_get_heap_size");
            auto used=(int64_t(*)(void))dlsym(RTLD_DEFAULT,"mono_gc_get_used_size");
            auto disabled=(int(*)(void))dlsym(RTLD_DEFAULT,"mono_unity_gc_is_disabled");
            auto minimum=(size_t(*)(void))dlsym(RTLD_DEFAULT,"GC_get_min_bytes_allocd");
            auto gcCount=(size_t(*)(void))dlsym(RTLD_DEFAULT,"GC_get_gc_no");
            auto incremental=(int(*)(void))dlsym(RTLD_DEFAULT,"GC_is_incremental_mode");
            auto automatic=(int*)dlsym(RTLD_DEFAULT,"GC_disable_automatic_collection");
            auto rootSize=(size_t*)dlsym(RTLD_DEFAULT,"GC_root_size");
            fprintf(stderr,"STEAM_GC_CONFIG minimum=%zu collections=%zu incremental=%d manual=%d roots=%zu\n",
                minimum?minimum():0,gcCount?gcCount():0,incremental?incremental():-1,automatic?*automatic:-1,rootSize?*rootSize:0);
            fprintf(stderr,"STEAM_MEMORY t=%.0f footprint_mib=%llu heap_mib=%lld used_mib=%lld gc_disabled=%d progress=%.4f appstate=%ld vm_result=%d\n",
                now,vm.phys_footprint>>20,heap?heap()>>20:-1,used?used()>>20:-1,disabled?disabled():-1,
                [s[@"progress"] doubleValue],(long)UIApplication.sharedApplication.applicationState,result);
        }
    }
    [self.host update:s];
}
- (void)playTapped {
    if(self.launched) return; NSDictionary *s=[self readSnapshot];
    if(![s[@"state"] isEqualToString:@"ready"] || [s[@"busy"] boolValue] || ![s[@"loggedIn"] boolValue] || ![s[@"canLaunch"] boolValue]) return;
    [self command:@{@"action":@"launch"}]; s=[self readSnapshot]; if(![s[@"launched"] boolValue]) return;
    self.launched=YES; [self.timer invalidate]; self.timer=nil; [self.view endEditing:YES]; [self.host releaseForGame]; UIApplication.sharedApplication.idleTimerDisabled=YES;
    setenv("BPP_GAME_ROOT",[s[@"runtimeRoot"] UTF8String],1); setenv("BPP_SELFTEST","1",1); setenv("BPP_FRAME_STATS","1",1);
    exposeGameVersion(); fprintf(stderr,"STEAM_APP_LAUNCH_GAME independent-session=1\n"); [engine runEmbeddedWithArgc:savedArgc argv:savedArgv appLaunchOpts:nil];
    // The engine owns its own window now; drop the launcher UI so its decoded artwork is freed.
    UIViewController *ui=self.host.viewController; [ui willMoveToParentViewController:nil]; [ui.view removeFromSuperview]; [ui removeFromParentViewController]; self.host=nil;
}
@end
@interface SteamScene : UIResponder <UIWindowSceneDelegate>
@property(nonatomic, strong) UIWindow *window;
@end
@implementation SteamScene
- (void)scene:(UIScene*)scene willConnectToSession:(UISceneSession*)session options:(UISceneConnectionOptions*)options {
    if (![scene isKindOfClass:UIWindowScene.class]) return;
    self.window = [[UIWindow alloc] initWithWindowScene:(UIWindowScene*)scene];
    self.window.rootViewController = [SteamController new]; [self.window makeKeyAndVisible];
}
- (void)sceneDidEnterBackground:(UIScene*)scene { if (engine.appController) [(id)engine.appController applicationDidEnterBackground:UIApplication.sharedApplication]; }
- (void)sceneWillEnterForeground:(UIScene*)scene { if (engine.appController) [(id)engine.appController applicationWillEnterForeground:UIApplication.sharedApplication]; }
- (void)sceneDidBecomeActive:(UIScene*)scene { if (engine.appController) [(id)engine.appController applicationDidBecomeActive:UIApplication.sharedApplication]; }
- (void)sceneWillResignActive:(UIScene*)scene { if (engine.appController) [(id)engine.appController applicationWillResignActive:UIApplication.sharedApplication]; }
@end
@interface SteamAppDelegate : UIResponder <UIApplicationDelegate>
@end
@implementation SteamAppDelegate
- (BOOL)application:(UIApplication*)application didFinishLaunchingWithOptions:(NSDictionary*)options { return YES; }
@end
int main(int argc,char **argv) {
    savedArgc = argc; savedArgv = argv;
    @autoreleasepool { return UIApplicationMain(argc,argv,nil,NSStringFromClass(SteamAppDelegate.class)); }
}
