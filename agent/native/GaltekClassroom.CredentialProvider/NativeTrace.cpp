#include "NativeTrace.h"

#include <vector>

namespace
{
    constexpr wchar_t kTraceRegistryKey[] = L"SOFTWARE\\Galtek\\Classroom\\CredentialProvider";
    constexpr wchar_t kTraceRegistryValue[] = L"NativeTraceFile";

    bool ReadTraceFilePath(std::wstring* path)
    {
        if (path == nullptr)
        {
            return false;
        }

        wchar_t envPath[MAX_PATH]{};
        DWORD envChars = GetEnvironmentVariableW(
            L"GALTEK_CP_NATIVE_TRACE_FILE",
            envPath,
            ARRAYSIZE(envPath));
        if (envChars > 0 && envChars < ARRAYSIZE(envPath))
        {
            *path = envPath;
            return !path->empty();
        }

        DWORD type = 0;
        DWORD bytes = 0;
        LSTATUS status = RegGetValueW(
            HKEY_LOCAL_MACHINE,
            kTraceRegistryKey,
            kTraceRegistryValue,
            RRF_RT_REG_SZ,
            &type,
            nullptr,
            &bytes);
        if (status != ERROR_SUCCESS || bytes <= sizeof(wchar_t))
        {
            return false;
        }

        std::vector<wchar_t> buffer(bytes / sizeof(wchar_t), L'\0');
        status = RegGetValueW(
            HKEY_LOCAL_MACHINE,
            kTraceRegistryKey,
            kTraceRegistryValue,
            RRF_RT_REG_SZ,
            &type,
            buffer.data(),
            &bytes);
        if (status != ERROR_SUCCESS || buffer.empty() || buffer[0] == L'\0')
        {
            return false;
        }

        *path = buffer.data();
        return !path->empty();
    }

    std::wstring UtcPrefix()
    {
        SYSTEMTIME utc{};
        GetSystemTime(&utc);

        wchar_t buffer[40]{};
        swprintf_s(
            buffer,
            L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ ",
            utc.wYear,
            utc.wMonth,
            utc.wDay,
            utc.wHour,
            utc.wMinute,
            utc.wSecond,
            utc.wMilliseconds);
        return buffer;
    }

    void AppendTraceFile(const std::wstring& line)
    {
        std::wstring path;
        if (!ReadTraceFilePath(&path))
        {
            return;
        }

        HANDLE file = CreateFileW(
            path.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return;
        }

        std::wstring entry = UtcPrefix();
        entry.append(line);
        if (entry.size() < 2 || entry.substr(entry.size() - 2) != L"\r\n")
        {
            entry.append(L"\r\n");
        }

        const int required = WideCharToMultiByte(
            CP_UTF8,
            0,
            entry.c_str(),
            -1,
            nullptr,
            0,
            nullptr,
            nullptr);
        if (required > 1)
        {
            std::vector<char> utf8(static_cast<size_t>(required));
            const int converted = WideCharToMultiByte(
                CP_UTF8,
                0,
                entry.c_str(),
                -1,
                utf8.data(),
                required,
                nullptr,
                nullptr);
            if (converted > 1)
            {
                DWORD written = 0;
                WriteFile(file, utf8.data(), static_cast<DWORD>(converted - 1), &written, nullptr);
            }
        }

        CloseHandle(file);
    }
}

void GaltekTraceLine(const std::wstring& line)
{
    std::wstring output = L"GaltekCredentialProvider ";
    output.append(line);
    if (output.size() < 2 || output.substr(output.size() - 2) != L"\r\n")
    {
        output.append(L"\r\n");
    }

    OutputDebugStringW(output.c_str());
    AppendTraceFile(output);
}

void GaltekTraceEvent(const wchar_t* eventName)
{
    if (eventName == nullptr)
    {
        return;
    }

    GaltekTraceLine(eventName);
}

void GaltekTraceEventWithAccount(const wchar_t* eventName, const std::string& accountId)
{
    if (eventName == nullptr)
    {
        return;
    }

    std::wstring line = eventName;
    if (!accountId.empty())
    {
        line.append(L" accountId=");
        for (char value : accountId)
        {
            if (value >= 0x20 && value <= 0x7E)
            {
                line.push_back(static_cast<wchar_t>(value));
            }
        }
    }

    GaltekTraceLine(line);
}
