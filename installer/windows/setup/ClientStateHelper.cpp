#include <windows.h>
#include <msi.h>
#include <msiquery.h>
#include <string>
#include <vector>

#include "GeneratedStateCommands.h"

namespace {
void LogMsiLine(MSIHANDLE install, const std::wstring& line) {
    if (line.empty()) {
        return;
    }

    const MSIHANDLE record = MsiCreateRecord(0);
    if (record == 0) {
        return;
    }
    MsiRecordSetStringW(record, 0, line.c_str());
    MsiProcessMessage(install, INSTALLMESSAGE_INFO, record);
    MsiCloseHandle(record);
}

std::wstring Utf8ToWide(const std::string& value) {
    if (value.empty()) {
        return std::wstring();
    }

    const int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
                                              static_cast<int>(value.size()), nullptr, 0);
    const UINT codePage = required > 0 ? CP_UTF8 : CP_OEMCP;
    const DWORD flags = required > 0 ? MB_ERR_INVALID_CHARS : 0;
    const int convertedLength = MultiByteToWideChar(codePage, flags, value.data(),
                                                     static_cast<int>(value.size()), nullptr, 0);
    if (convertedLength <= 0) {
        return L"GALTEK_STATE_HELPER_OUTPUT_DECODE_FAILED";
    }

    std::wstring converted(static_cast<size_t>(convertedLength), L'\0');
    MultiByteToWideChar(codePage, flags, value.data(), static_cast<int>(value.size()),
                        converted.data(), convertedLength);
    return converted;
}

void LogCapturedOutput(MSIHANDLE install, const std::string& output) {
    const std::wstring text = Utf8ToWide(output);
    size_t offset = 0;
    while (offset < text.size()) {
        const size_t end = text.find_first_of(L"\r\n", offset);
        const std::wstring line = text.substr(offset, end == std::wstring::npos ? std::wstring::npos : end - offset);
        LogMsiLine(install, line);
        if (end == std::wstring::npos) {
            break;
        }
        offset = end + 1;
        if (text[end] == L'\r' && offset < text.size() && text[offset] == L'\n') {
            ++offset;
        }
    }
}

DWORD RunEncodedPowerShell(MSIHANDLE install, const wchar_t* mode, const wchar_t* encodedCommand) {
    wchar_t systemDirectory[MAX_PATH]{};
    const UINT length = GetSystemDirectoryW(systemDirectory, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) {
        return GetLastError();
    }

    SECURITY_ATTRIBUTES security{};
    security.nLength = sizeof(security);
    security.bInheritHandle = TRUE;
    HANDLE outputRead = nullptr;
    HANDLE outputWrite = nullptr;
    if (!CreatePipe(&outputRead, &outputWrite, &security, 0)) {
        return GetLastError();
    }
    if (!SetHandleInformation(outputRead, HANDLE_FLAG_INHERIT, 0)) {
        const DWORD error = GetLastError();
        CloseHandle(outputRead);
        CloseHandle(outputWrite);
        return error;
    }

    std::wstring executable(systemDirectory);
    executable += L"\\WindowsPowerShell\\v1.0\\powershell.exe";
    std::wstring commandLine = L"\"" + executable +
        L"\" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " +
        encodedCommand;

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    startup.hStdOutput = outputWrite;
    startup.hStdError = outputWrite;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), commandLine.data(), nullptr, nullptr, TRUE,
                        CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) {
        const DWORD error = GetLastError();
        CloseHandle(outputRead);
        CloseHandle(outputWrite);
        return error;
    }

    CloseHandle(outputWrite);
    outputWrite = nullptr;
    std::string output;
    std::vector<char> buffer(4096);
    for (;;) {
        DWORD bytesRead = 0;
        if (!ReadFile(outputRead, buffer.data(), static_cast<DWORD>(buffer.size()), &bytesRead, nullptr)) {
            if (GetLastError() != ERROR_BROKEN_PIPE) {
                LogMsiLine(install, L"GALTEK_STATE_HELPER_OUTPUT_READ_FAILED");
            }
            break;
        }
        output.append(buffer.data(), bytesRead);
    }
    CloseHandle(outputRead);

    const DWORD wait = WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = ERROR_GEN_FAILURE;
    if (wait == WAIT_OBJECT_0) {
        GetExitCodeProcess(process.hProcess, &exitCode);
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);

    LogCapturedOutput(install, output);
    if (exitCode != ERROR_SUCCESS) {
        LogMsiLine(install, std::wstring(L"GALTEK_STATE_HELPER_FAILED mode=") + mode +
            L" exitCode=" + std::to_wstring(exitCode));
    }
    return exitCode;
}

UINT RunCustomAction(MSIHANDLE install, const wchar_t* mode, const wchar_t* encodedCommand) {
    const DWORD exitCode = RunEncodedPowerShell(install, mode, encodedCommand);
    return exitCode == ERROR_SUCCESS ? ERROR_SUCCESS : ERROR_INSTALL_FAILURE;
}
}

extern "C" __declspec(dllexport) UINT __stdcall CaptureClientState(MSIHANDLE install) {
    return RunCustomAction(install, L"capture", kGaltekCaptureStateCommand);
}

extern "C" __declspec(dllexport) UINT __stdcall RollbackClientStateFinal(MSIHANDLE install) {
    return RunCustomAction(install, L"rollback", kGaltekRollbackStateCommand);
}

extern "C" __declspec(dllexport) UINT __stdcall CommitClientStateFinal(MSIHANDLE install) {
    return RunCustomAction(install, L"commit", kGaltekCommitStateCommand);
}
