// BPP_TOUCH_DIAG=1: logs what UIKit hands UnityView for each touch, to debug touch/orientation mapping.
#import <UIKit/UIKit.h>
#import <objc/runtime.h>

void bpp_log(const char *fmt, ...);

static IMP g_orig_began;

static void diag_touches_began(UIView *self, SEL cmd, NSSet *touches, UIEvent *event) {
    UITouch *t = touches.anyObject;
    CGPoint inView = [t locationInView:self], inWindow = [t locationInView:nil];
    UIWindowScene *scene = self.window.windowScene;
    bpp_log("touch view=(%.0f,%.0f) window=(%.0f,%.0f) bounds=%.0fx%.0f windowBounds=%.0fx%.0f scale=%.1f sceneOrientation=%ld",
           inView.x, inView.y, inWindow.x, inWindow.y, self.bounds.size.width, self.bounds.size.height,
           self.window.bounds.size.width, self.window.bounds.size.height, self.contentScaleFactor,
           (long)scene.interfaceOrientation);
    ((void (*)(id, SEL, NSSet *, UIEvent *))g_orig_began)(self, cmd, touches, event);
}

__attribute__((constructor)) static void ui_diag_init(void) {
    const char *on = getenv("BPP_TOUCH_DIAG");
    if (!on || strcmp(on, "1") != 0) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        Class view = objc_getClass("UnityView");
        Method m = view ? class_getInstanceMethod(view, @selector(touchesBegan:withEvent:)) : NULL;
        if (!m) { bpp_log("touch diag: UnityView not found"); return; }
        g_orig_began = method_setImplementation(m, (IMP)diag_touches_began);
        bpp_log("touch diag installed");
    });
}
