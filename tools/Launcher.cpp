#define UNICODE
#define _UNICODE
#include <windows.h>
#include <string>

// Tiny native launcher: no CLR startup, extraction, console or installer.
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    wchar_t path[32768];
    DWORD length = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
    if (!length || length >= ARRAYSIZE(path)) return 1;
    std::wstring folder(path, length);
    folder.resize(folder.find_last_of(L"\\/"));
    std::wstring executable = folder + L"\\app\\WireLink.exe";
    std::wstring command = L"\"" + executable + L"\"";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
        0, nullptr, folder.c_str(), &startup, &process)) {
        MessageBoxW(nullptr, L"无法启动 WireLink。请保留完整的 app 文件夹，并将整个发布目录复制到本地后重试。",
            L"WireLink", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    return 0;
}
