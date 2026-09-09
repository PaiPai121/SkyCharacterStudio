// Exercise the actual redirect hook with a simulated engine memory stream.
// This does not launch or inject into the game.
#include "scene_redirect.cpp"
#include <cassert>
#include <fstream>
#include <vector>
static std::string fallback;
static void* FakeAlloc(void*, size_t n) { return malloc(n); }
static void* FakeOpen(void*, const char* name, unsigned, unsigned, unsigned short) {
    fallback = name;
    return nullptr;
}
static void* FakeMsOpen(void* ms, const char*, unsigned, unsigned) { return ms; }
int main() {
    auto region = VirtualAlloc(nullptr, 0xb00000, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    assert(region);
    g_base = reinterpret_cast<uintptr_t>(region);
    // x64 absolute jump from the simulated engine allocator to the test allocator.
    unsigned char jump[] = {0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xe0};
    auto allocator = reinterpret_cast<uintptr_t>(&FakeAlloc);
    memcpy(jump + 2, &allocator, sizeof(allocator));
    memcpy((void*)(g_base + 0x4aa650), jump, sizeof(jump));
    FlushInstructionCache(GetCurrentProcess(), region, 0xb00000);
    *(void**)(g_base + 0xae0db0) = region;
    *(void**)(g_base + 0x9d5d20 + 0x28) = (void*)&FakeMsOpen;
    o_Open = FakeOpen;
    g_redirect_root = L"D:\\SteamLibrary\\steamapps\\common\\Sora No Kiseki the 1st\\ED9Loader\\cache\\merged\\";
    std::ifstream f((g_redirect_root + L"script\\ScherazardSummon.dat").c_str(), std::ios::binary);
    std::vector<char> expected((std::istreambuf_iterator<char>(f)), {});
    assert(!expected.empty());
    alignas(void*) char self[128] = {};
    auto ms = (char*)hk_Open(self, "script/scena/ScherazardSummon.dat", 0, 0, 0);
    assert(ms && *(uint64_t*)(ms + 0x10) == expected.size());
    assert(memcmp(*(void**)(ms + 0x18), expected.data(), expected.size()) == 0);
    assert(*(void**)(self + 0x28) == ms);
    free(*(void**)(ms + 0x18)); free(ms);
    hk_Open(self, "asset/common/model/chr5002_original_test_animation", 0, 0, 0);
    assert(fallback == "asset/common/model/chr5002_test_animation");
    hk_Open(self, "script/scena/__nonexistent_test.dat", 0, 0, 0);
    assert(fallback == "script/scena/__nonexistent_test.dat");
    VirtualFree(region, 0, MEM_RELEASE);
    puts("PASS: real cached DAT routed byte-for-byte; alias and missing-file fallback valid (simulated engine)");
}
