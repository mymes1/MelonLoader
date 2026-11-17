#include "AndroidData.h"

#include <jni.h>
#include <unistd.h>
#include <android/log.h>
#include <stdio.h>
#include <string>
#include <cstring>
#include <sys/stat.h>
#include <fcntl.h>

#include "../Utils/Console/Debug.h"
#include "../Utils/Assertion.h"
#include "../Core.h"

char* AndroidData::BaseDataDir = "/storage/emulated/0/Android/data";
char* AndroidData::AppName = NULL;
char* AndroidData::DataDir = NULL;

bool AndroidData::Initialize()
{
    GetAppName();
    if (!Assertion::ShouldContinue) return Assertion::ShouldContinue;
    GetDataDir();
    return Assertion::ShouldContinue;
}

void AndroidData::GetAppName()
{
    char buffer[0x1000];
    int fd = open("/proc/self/cmdline", O_RDONLY);
    if (fd < 0)
    {
        Assertion::ThrowInternalFailure("Cannot open process command line");
        return;
    }
    ssize_t count = read(fd, buffer, sizeof(buffer) - 1);
    close(fd);
    if (count <= 0)
    {
        Assertion::ThrowInternalFailure("Cannot get App name");
        return;
    }
    buffer[count] = '\0';
    size_t length = strlen(buffer);
    AppName = new char[length + 1];
    memcpy(AppName, buffer, length + 1);
}

void AndroidData::GetDataDir()
{
    auto env = Core::GetEnv();

    auto klass = env->FindClass("com/melonloader/ApplicationState");
    if (klass == nullptr) {
        Assertion::ThrowInternalFailure("Failed to find com/melonloader/ApplicationState");
        return;
    }

    auto fieldId = env->GetStaticFieldID(klass, "BaseDirectory", "Ljava/lang/String;");
    if (fieldId == nullptr) {
        Assertion::ThrowInternalFailure("Failed to find com/melonloader/ApplicationState::BaseDirectory");
        return;
    }

    auto jStr = env->GetStaticObjectField(klass, fieldId);
    if (jStr == nullptr) {
        Assertion::ThrowInternalFailure("com.melonloader.ApplicationState::BaseDirectory is null");
        return;
    }

    const char* cStr = env->GetStringUTFChars((jstring)jStr, nullptr);
    if (cStr == nullptr) {
        env->DeleteLocalRef(jStr);
        Assertion::ThrowInternalFailure("Failed to read BaseDirectory");
        return;
    }
    const size_t cStrSize = strlen(cStr);
    DataDir = new char[cStrSize + 1];
    std::strcpy(DataDir, cStr);
    DataDir[cStrSize] = '\0';

    env->ReleaseStringUTFChars((jstring)jStr, cStr);
    env->DeleteLocalRef(jStr);
}
