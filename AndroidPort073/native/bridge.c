#include <android/log.h>
#include <dlfcn.h>
#include <jni.h>

#define TAG "MelonLoader073"
typedef int (*loader_init)(void *, void *, const char *);

JNIEXPORT jboolean JNICALL
Java_com_melonloader_port073_Loader073_initializeNative(JNIEnv *env, jclass klass, jstring path)
{
    (void)klass;
    if (!path) return JNI_FALSE;

    // Do not force-load Unity: the patch must run after Unity has loaded it, but
    // before it resolves il2cpp_init. Those lifecycle constraints need testing.
    void *unity = dlopen("libunity.so", RTLD_NOW | RTLD_NOLOAD);
    if (!unity) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "libunity.so is not loaded: %s", dlerror());
        return JNI_FALSE;
    }
    void *bootstrap = dlopen("libMelonLoader.Bootstrap.so", RTLD_NOW);
    if (!bootstrap) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "Cannot load 0.7.3 bootstrap: %s", dlerror());
        dlclose(unity);
        return JNI_FALSE;
    }
    loader_init init = (loader_init)dlsym(bootstrap, "MelonLoaderAndroidInitialize");
    const char *base = (*env)->GetStringUTFChars(env, path, NULL);
    if (!init || !base) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "Missing bootstrap entry point or path");
        if (base) (*env)->ReleaseStringUTFChars(env, path, base);
        dlclose(bootstrap);
        dlclose(unity);
        return JNI_FALSE;
    }
    int result = init(bootstrap, unity, base);
    (*env)->ReleaseStringUTFChars(env, path, base);
    // Keep both libraries loaded, including on partial initialization failure:
    // hooks or managed delegates may already refer to them.
    return result ? JNI_TRUE : JNI_FALSE;
}
