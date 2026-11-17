"""Compile the JNI shim against host stubs, not an Android runtime."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class BridgeTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which("cc"), "C compiler not installed")
    def test_bridge_handles_missing_components_and_keeps_loaded_handles(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "android").mkdir()
            (root / "android/log.h").write_text('''
#define ANDROID_LOG_ERROR 6
int __android_log_print(int, const char *, const char *, ...);
''')
            (root / "jni.h").write_text('''
#pragma once
typedef int jboolean;
typedef void *jclass;
typedef void *jstring;
#define JNIEXPORT
#define JNICALL
#define JNI_TRUE 1
#define JNI_FALSE 0
struct JNINativeInterface_ {
  const char *(*GetStringUTFChars)(const struct JNINativeInterface_ **, jstring, void *);
  void (*ReleaseStringUTFChars)(const struct JNINativeInterface_ **, jstring, const char *);
};
typedef const struct JNINativeInterface_ *JNIEnv;
''')
            (root / "dlfcn.h").write_text('''
#define RTLD_NOW 2
#define RTLD_NOLOAD 4
void *dlopen(const char *, int);
void *dlsym(void *, const char *);
int dlclose(void *);
const char *dlerror(void);
''')
            (root / "test.c").write_text(r'''
#include <string.h>
#include <stddef.h>
#include "jni.h"
static int mode, closed, released, called;
int __android_log_print(int p, const char *t, const char *f, ...) { (void)p;(void)t;(void)f; return 0; }
const char *dlerror(void) { return "test"; }
void *dlopen(const char *name, int flags) {
    (void)flags;
    if (strstr(name,"libunity")) return mode == 1 ? NULL : (void *)1;
    return mode == 2 ? NULL : (void *)2;
}
int dlclose(void *p) { (void)p; closed++; return 0; }
static int init(void *a, void *b, const char *p) {
    called++; return a == (void *)2 && b == (void *)1 && strcmp(p,"/data/game") == 0;
}
void *dlsym(void *p, const char *s) { (void)p; (void)s; return mode == 3 ? NULL : (void *)init; }
const char *get(JNIEnv *env, jstring s, void *x) { (void)env;(void)s;(void)x; return mode == 4 ? NULL : "/data/game"; }
void release(JNIEnv *env, jstring s, const char *p) { (void)env;(void)s;(void)p; released++; }
#include "bridge.c"
int main(void) {
    struct JNINativeInterface_ table = {get,release};
    JNIEnv env = &table;
    for (mode = 1; mode <= 4; mode++) {
        closed = released = called = 0;
        if (Java_com_melonloader_port073_Loader073_initializeNative(&env, NULL, (void *)1)) return 1;
        if (mode == 1 && closed != 0) return 2;
        if (mode == 2 && closed != 1) return 3;
        if (mode == 3 && (closed != 2 || released != 1)) return 4;
        if (mode == 4 && (closed != 2 || released != 0)) return 5;
        if (called != 0) return 6;
    }
    mode = closed = released = called = 0;
    if (!Java_com_melonloader_port073_Loader073_initializeNative(&env, NULL, (void *)1)) return 7;
    if (closed != 0 || released != 1 || called != 1) return 8;
    return 0;
}
''')
            executable = root / "test"
            subprocess.run(["cc", "-std=c11", "-Wall", "-Wextra", "-Werror",
                            "-I", str(root), "-I", str(ROOT / "AndroidPort073/native"),
                            str(root / "test.c"), "-o", str(executable)], check=True)
            subprocess.run([str(executable)], check=True)


if __name__ == "__main__":
    unittest.main()
