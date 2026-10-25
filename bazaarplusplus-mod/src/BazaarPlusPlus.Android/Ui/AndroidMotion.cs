using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

internal static class AndroidMotion
{
    // Read on each interaction so changing Android's Remove animations takes effect immediately.
    public static bool AnimationsEnabled()
    {
        var animator = IntPtr.Zero;
        try
        {
            // Use the non-generic JNI entry point: IL2CPP need not have retained a
            // CallStatic<bool> instantiation for this game's own Java calls.
            animator = AndroidJNISafe.FindClass("android/animation/ValueAnimator");
            var method = AndroidJNISafe.GetStaticMethodID(animator, "areAnimatorsEnabled", "()Z");
            return AndroidJNISafe.CallStaticBooleanMethod(
                animator,
                method,
                new Il2CppStructArray<jvalue>(0L)
            );
        }
        catch
        {
            // No reliable preference: keep navigation usable without spatial motion.
            return false;
        }
        finally
        {
            if (animator != IntPtr.Zero)
                AndroidJNISafe.DeleteLocalRef(animator);
        }
    }
}
