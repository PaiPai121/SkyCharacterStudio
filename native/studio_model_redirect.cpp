// Redirect merged asset files through the game's own file-open routine.
// The function is found by a unique instruction signature instead of a
// version-specific RVA. Only files already present in ED9Loader's merged
// cache are redirected; every other request follows the original path.
#include "ed9loader_api.h"

#include <Windows.h>
#include <cstdio>
#include <cstring>
#include <string>

namespace {
using Open = void* (__fastcall*)(void*, const char*, unsigned, unsigned, unsigned short);
Open originalOpen = nullptr;
const Ed9Api* host = nullptr;
std::string mergedRoot;

bool IsSafeAssetPath(const char* name) {
    if (!name || _strnicmp(name, "asset/", 6) != 0) return false;
    if (name[0] == '/' || name[0] == '\\' || std::strchr(name, ':')) return false;
    const char* segment = name;
    for (const char* p = name; ; ++p) {
        if (*p == '/' || *p == '\\' || *p == '\0') {
            if (p == segment || (p - segment == 2 && segment[0] == '.' && segment[1] == '.'))
                return false;
            if (*p == '\0') break;
            segment = p + 1;
        }
    }
    return true;
}

void* __fastcall RedirectOpen(void* self, const char* name, unsigned a3, unsigned a4,
                              unsigned short a5) {
    if (IsSafeAssetPath(name)) {
        std::string candidate = mergedRoot + name;
        for (char& ch : candidate) if (ch == '/') ch = '\\';
        const DWORD attributes = GetFileAttributesA(candidate.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY)) {
            if (host && host->log) {
                char message[384];
                std::snprintf(message, sizeof(message), "StudioModelRedirect: %s", name);
                host->log(message);
            }
            return originalOpen(self, candidate.c_str(), a3, a4, a5);
        }
    }
    return originalOpen(self, name, a3, a4, a5);
}

void* FindUniqueOpenSignature(unsigned char* base) {
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || dos->e_lfanew > 0x1000)
        return nullptr;
    const auto* pe = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (pe->Signature != IMAGE_NT_SIGNATURE || pe->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64)
        return nullptr;
    constexpr unsigned char signature[] = {
        0x48, 0x89, 0x5c, 0x24, 0x20, 0x55, 0x56, 0x57, 0x41, 0x56,
        0x41, 0x57, 0x48, 0x8d, 0xac, 0x24, 0x90, 0xfc, 0xff, 0xff
    };
    const auto* sections = IMAGE_FIRST_SECTION(pe);
    void* found = nullptr;
    unsigned matches = 0;
    for (unsigned i = 0; i < pe->FileHeader.NumberOfSections; ++i) {
        const auto& section = sections[i];
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        const auto start = section.VirtualAddress;
        const auto size = section.Misc.VirtualSize;
        if (start >= pe->OptionalHeader.SizeOfImage ||
            size > pe->OptionalHeader.SizeOfImage - start || size < sizeof(signature)) continue;
        const auto* bytes = base + start;
        for (size_t offset = 0; offset <= size - sizeof(signature); ++offset) {
            if (std::memcmp(bytes + offset, signature, sizeof(signature)) == 0) {
                found = const_cast<unsigned char*>(bytes + offset);
                if (++matches > 1) return nullptr;
            }
        }
    }
    return matches == 1 ? found : nullptr;
}
} // namespace

extern "C" __declspec(dllexport) void Plugin_Load(const Ed9Api* api) {
    if (!api || api->abi_version < 2 || !api->log || !api->get_module_base || !api->install_hook)
        return;
    host = api;
    char executable[32768] = {};
    const DWORD length = GetModuleFileNameA(nullptr, executable, sizeof(executable));
    if (!length || length >= sizeof(executable)) {
        api->log("StudioModelRedirect: cannot determine game path; redirect disabled");
        return;
    }
    char* slash = std::strrchr(executable, '\\');
    if (!slash) {
        api->log("StudioModelRedirect: invalid game path; redirect disabled");
        return;
    }
    *(slash + 1) = '\0';
    mergedRoot = std::string(executable) + "ED9Loader\\cache\\merged\\";
    void* target = FindUniqueOpenSignature(static_cast<unsigned char*>(api->get_module_base()));
    if (!target) {
        api->log("StudioModelRedirect: file-open signature missing or ambiguous; redirect disabled");
        return;
    }
    const int result = api->install_hook(target, reinterpret_cast<void*>(RedirectOpen),
                                         reinterpret_cast<void**>(&originalOpen));
    char message[160];
    std::snprintf(message, sizeof(message),
                  "StudioModelRedirect: file-open hook result=%d (0=success)", result);
    api->log(message);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
