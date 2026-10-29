#import <UIKit/UIKit.h>
extern int bpp_steam_save_session(const char*); extern char *bpp_steam_load_session(void);
@interface ProbeScene : UIResponder <UIWindowSceneDelegate>
@end
@implementation ProbeScene
- (void)scene:(UIScene*)scene willConnectToSession:(UISceneSession*)session options:(UISceneConnectionOptions*)options {
    int status=bpp_steam_save_session("keychain-probe"); char *value=bpp_steam_load_session();
    int ok=status==0 && value && strcmp(value,"keychain-probe")==0;
    free(value);
    ok &= bpp_steam_save_session("keychain-probe-updated")==0;
    value=bpp_steam_load_session();
    ok &= value && strcmp(value,"keychain-probe-updated")==0; free(value);
    ok &= bpp_steam_save_session("")==0;
    value=bpp_steam_load_session(); ok &= value==NULL; free(value);
    printf("STEAM_KEYCHAIN_PROBE %s status=%d\n",ok?"PASS":"FAIL",status); fflush(stdout);

}
@end
@interface ProbeDelegate : UIResponder <UIApplicationDelegate>
@end
@implementation ProbeDelegate
@end
int main(int argc,char**argv){@autoreleasepool{return UIApplicationMain(argc,argv,nil,NSStringFromClass(ProbeDelegate.class));}}
