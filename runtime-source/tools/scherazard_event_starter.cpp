// ED9Loader plugin: keyboard-triggered Scherazard summon.
//
// The script functions are injected by ScriptInject and include the two
// Scherazard model variants plus stock Estelle/Joshua entries. This plugin
// handles input edges and starts the selected event on the game's main thread,
// after the normal field update for the frame.
// The defaults below match the current Steam executable (sora_1st.exe
// 1.0.5.0).  ED9ModManager's bundled EventStarter uses the same addresses;
// they can still be overridden in ED9Loader/config/EventStarter.ini after a
// future game update.

#include "../vendor/ed9modmanager/ed9loader_api.h"

#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>

static const Ed9Api* g_api = nullptr;

static unsigned g_rva_spawn = 0x24afa0;  // current event starter function
static unsigned g_rva_scrmgr = 0xae0d38; // current script-manager global
static unsigned g_rva_frame = 0x23dd10;  // current field manager frame update
static unsigned g_hotkey_vk = VK_F8;
static unsigned g_toggle_vk = VK_F9;

using FnSpawn = void (*)(void* mgr, const char* name, void* p3, unsigned p4);
using FnFrame = void (*)(void* p1, void* p2);

static FnSpawn g_spawn = nullptr;
static FnFrame g_frame_orig = nullptr;
static volatile LONG g_pending = 0;
// Queue on the key edge, then wait one complete field frame before calling
// the engine's event-thread launcher.  This keeps the launcher out of the
// frame in which the field manager is processing the key and scene state.
static volatile LONG g_pending_frames = 0;
static char g_pending_name[64] = "ScherazardSummon";
static bool g_key_down = false;
static bool g_toggle_key_down = false;
static volatile LONG g_model_mode = 1; // 1=edited/subtle, 0=original
static volatile LONG g_character = 0;  // 0=Scherazard, 1=Estelle, 2=Joshua
static volatile LONG g_field_frames = 0;
static volatile LONG g_observer_started = 0;

// Observe input independently of field updates. This diagnoses a paused field
// without launching engine events from a background thread.
static DWORD WINAPI ObserveInput(void*) {
    bool previous = false;
    LONG previousFrames = 0;
    for (;;) {
        const bool down = (GetAsyncKeyState(static_cast<int>(g_hotkey_vk)) & 0x8000) != 0;
        const LONG frames = InterlockedCompareExchange(&g_field_frames, 0, 0);
        if (down && !previous) {
            DWORD foregroundPid = 0;
            GetWindowThreadProcessId(GetForegroundWindow(), &foregroundPid);
            char msg[256] = {};
            _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                "EventStarter diagnostic: tick=%llu F8 observed; game_foreground=%d field_frames=%ld advancing=%d pending=%ld",
                static_cast<unsigned long long>(GetTickCount64()),
                foregroundPid == GetCurrentProcessId(), frames, frames != previousFrames,
                InterlockedCompareExchange(&g_pending, 0, 0));
            if (g_api && g_api->log) g_api->log(msg);
        }
        previous = down;
        previousFrames = frames;
        Sleep(15);
    }
}

static uintptr_t Base() {
    return reinterpret_cast<uintptr_t>(g_api->get_module_base());
}

template <typename T>
static bool Read(uintptr_t address, T* out) {
    return g_api != nullptr && g_api->safe_read != nullptr &&
           g_api->safe_read(reinterpret_cast<const void*>(address), out, sizeof(T)) != 0;
}

static void Print(const char* message) {
    if (g_api != nullptr && g_api->console_print != nullptr) g_api->console_print(message);
    if (g_api != nullptr && g_api->log != nullptr) g_api->log(message);
}

static void QueueEvent(const char* name) {
    if (name == nullptr || name[0] == '\0') name = "ScherazardSummon";
    strncpy_s(g_pending_name, sizeof(g_pending_name), name, _TRUNCATE);
    if (InterlockedCompareExchange(&g_pending, 1, 0) == 0) {
        InterlockedExchange(&g_pending_frames, 1);
        char msg[160] = {};
        _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                    "EventStarter: queued '%s' (runs after one complete field frame)\n",
                    g_pending_name);
        Print(msg);
    }
}

static void SetModelMode(LONG mode, bool persist) {
    mode = mode != 0 ? 1 : 0;
    InterlockedExchange(&g_model_mode, mode);
    if (persist && g_api != nullptr && g_api->cfg_set_int != nullptr)
        g_api->cfg_set_int("EventStarter", "model_mode", static_cast<int>(mode));

    char msg[192] = {};
    _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                "EventStarter: Scherazard model=%s (press F8 to summon/recreate)\n",
                mode != 0 ? "edited/subtle" : "original");
    Print(msg);
}

static const char* CharacterName(LONG character) {
    switch (character) {
    case 1: return "Estelle";
    case 2: return "Joshua";
    case 3: return "CharacterStudio installed selection";
    default: return "Scherazard";
    }
}

static const char* EventNameFor(LONG character, LONG modelMode) {
    switch (character) {
    case 1: return "EstelleSummon";
    case 2: return "JoshuaSummon";
    case 3: return modelMode != 0 ? "StudioSummon" : "StudioSummonOriginal";
    default:
        return modelMode != 0 ? "ScherazardSummon" : "ScherazardSummonOriginal";
    }
}

static const char* SelectedEventName() {
    const LONG character = InterlockedCompareExchange(&g_character, 0, 0);
    const LONG modelMode = InterlockedCompareExchange(&g_model_mode, 0, 0);
    return EventNameFor(character, modelMode);
}

static void SetCharacterMode(LONG character, bool persist) {
    if (character < 0 || character > 3) character = 0;
    InterlockedExchange(&g_character, character);
    if (persist && g_api != nullptr && g_api->cfg_set_int != nullptr)
        g_api->cfg_set_int("EventStarter", "character", static_cast<int>(character));

    char msg[192] = {};
    _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                "EventStarter: character=%s (press F8 to summon/recreate)\n",
                CharacterName(character));
    Print(msg);
}

static void RunPendingOnMainThread() {
    LONG frames = InterlockedCompareExchange(&g_pending_frames, 0, 0);
    if (frames > 0) {
        InterlockedDecrement(&g_pending_frames);
        return;
    }
    if (InterlockedCompareExchange(&g_pending, 0, 1) != 1) return;
    if (g_spawn == nullptr) return;

    uintptr_t manager = 0;
    if (!Read(Base() + g_rva_scrmgr, &manager) || manager == 0) {
        Print("EventStarter: no script manager (enter a field scene first)\n");
        return;
    }

    char name[64] = {};
    strncpy_s(name, sizeof(name), g_pending_name, _TRUNCATE);

    // Spawn silently returns when the function is absent or the event VM is
    // occupied. Check both prerequisites before reporting a submission.
    using FindFunction = int (*)(void*, const char*);
    auto findFunction = reinterpret_cast<FindFunction>(Base() + 0x4b76e0);
    auto findEvent = [&]() -> bool {
        uintptr_t list = 0;
        uint32_t count = 0;
        if (!Read(manager + 0x160, &list) || !Read(manager + 0x168, &count) ||
            !list || count > 64) {
            Print("EventStarter: invalid loaded-script list\n");
            return false;
        }
        char detail[256] = {};
        _snprintf_s(detail, sizeof(detail), _TRUNCATE,
                    "EventStarter: loaded-script count=%u\n", count);
        Print(detail);
        bool found = false;
        for (uint32_t i = 0; i < count; ++i) {
            uintptr_t script = 0, data = 0;
            if (!Read(list + i * sizeof(uintptr_t), &script) || !script ||
                !Read(script + 0x30, &data) || !data) continue;
            char path[128] = {};
            for (size_t j = 0; j + 1 < sizeof(path); ++j) {
                if (!Read(script + 0xc + j, &path[j]) || !path[j]) break;
            }
            const int index = findFunction(reinterpret_cast<void*>(script), name);
            _snprintf_s(detail, sizeof(detail), _TRUNCATE,
                        "EventStarter: script[%u]=%s event_index=%d\n", i, path, index);
            Print(detail);
            found |= index != -1;
        }
        return found;
    };
    if (!findEvent()) {
        Print("EventStarter: event missing; loading ScherazardSummon on field thread\n");
        using LoadScript = uint64_t (*)(void*, const char*);
        // Calls the installed ScriptInject hook too, preserving its list capacity.
        auto loadScript = reinterpret_cast<LoadScript>(Base() + 0x24aeb0);
        loadScript(reinterpret_cast<void*>(manager), "ScherazardSummon");
        if (!findEvent()) {
            Print("EventStarter: summon aborted: event still missing after script load\n");
            return;
        }
    }
    uintptr_t engine = 0, eventVm = 0;
    unsigned char busy = 0;
    if (!Read(Base() + 0xae0d88, &engine) || !engine ||
        !Read(engine + 0x11688, &eventVm) || !eventVm ||
        !Read(eventVm + 0x408, &busy)) {
        Print("EventStarter: summon aborted: event VM unavailable\n");
        return;
    }
    if (busy) {
        Print("EventStarter: summon blocked: event VM busy (menu/cutscene/event); no state forced\n");
        return;
    }
    g_spawn(reinterpret_cast<void*>(manager), name, nullptr, 0);

    char msg[160] = {};
    _snprintf_s(msg, sizeof(msg), _TRUNCATE, "EventStarter: submitted '%s'; actor creation NOT confirmed; tick=%llu\n", name,
                static_cast<unsigned long long>(GetTickCount64()));
    Print(msg);
}

static void __cdecl Frame_Detour(void* p1, void* p2) {
    if (g_frame_orig != nullptr) g_frame_orig(p1, p2);
    InterlockedIncrement(&g_field_frames);

    const bool down = (GetAsyncKeyState(static_cast<int>(g_hotkey_vk)) & 0x8000) != 0;
    if (down && !g_key_down) {
        Print("EventStarter diagnostic: F8 received on field thread\n");
        QueueEvent(SelectedEventName());
    }
    g_key_down = down;

    const bool toggleDown = (GetAsyncKeyState(static_cast<int>(g_toggle_vk)) & 0x8000) != 0;
    if (toggleDown && !g_toggle_key_down) {
        const LONG current = InterlockedCompareExchange(&g_model_mode, 0, 0);
        SetModelMode(current == 0 ? 1 : 0, true);
    }
    g_toggle_key_down = toggleDown;
    if (g_pending != 0) RunPendingOnMainThread();
}

static void Cmd_Scherazard(int argc, const char** argv) {
    if (argc >= 2 && argv != nullptr && argv[1] != nullptr && argv[1][0] != '\0')
        QueueEvent(argv[1]);
    else
        QueueEvent(SelectedEventName());
}

static void Cmd_ModelMode(int argc, const char** argv) {
    if (argc >= 2 && argv != nullptr && argv[1] != nullptr) {
        if (_stricmp(argv[1], "original") == 0 || strcmp(argv[1], "0") == 0) {
            SetModelMode(0, true);
            return;
        }
        if (_stricmp(argv[1], "edited") == 0 || _stricmp(argv[1], "subtle") == 0 ||
            strcmp(argv[1], "1") == 0) {
            SetModelMode(1, true);
            return;
        }
    }
    if (g_api != nullptr && g_api->console_print != nullptr)
        g_api->console_print("usage: -scherazard-mode original|edited\n");
}

static void Cmd_Character(int argc, const char** argv) {
    if (argc >= 2 && argv != nullptr && argv[1] != nullptr) {
        if (_stricmp(argv[1], "studio") == 0 || strcmp(argv[1], "3") == 0) {
            SetCharacterMode(3, true);return;
        }
        if (_stricmp(argv[1], "scherazard") == 0 || _stricmp(argv[1], "shera") == 0 ||
            strcmp(argv[1], "0") == 0) {
            SetCharacterMode(0, true);
            return;
        }
        if (_stricmp(argv[1], "estelle") == 0 || strcmp(argv[1], "1") == 0) {
            SetCharacterMode(1, true);
            return;
        }
        if (_stricmp(argv[1], "joshua") == 0 || strcmp(argv[1], "2") == 0) {
            SetCharacterMode(2, true);
            return;
        }
    }
    if (g_api != nullptr && g_api->console_print != nullptr)
        g_api->console_print("usage: -scherazard-character scherazard|estelle|joshua\n");
}

// These exports are intentionally tiny and have no dependency on the loader's
// C++ internals.  BracerMentorHud resolves them at runtime so the HUD can use
// the same queue, model state and main-thread scheduling as F8/F9.
extern "C" __declspec(dllexport) void EventStarter_SummonSelected() {
    QueueEvent(SelectedEventName());
}

extern "C" __declspec(dllexport) void EventStarter_SetModelMode(int mode) {
    SetModelMode(mode != 0 ? 1 : 0, true);
}

extern "C" __declspec(dllexport) void EventStarter_SetCharacter(int character) {
    SetCharacterMode(static_cast<LONG>(character), true);
}

extern "C" __declspec(dllexport) int EventStarter_GetModelMode() {
    return InterlockedCompareExchange(&g_model_mode, 0, 0) != 0 ? 1 : 0;
}

extern "C" __declspec(dllexport) int EventStarter_GetCharacter() {
    return static_cast<int>(InterlockedCompareExchange(&g_character, 0, 0));
}

extern "C" __declspec(dllexport) void Plugin_Load(const Ed9Api* api) {
    if (api == nullptr || api->log == nullptr || api->get_module_base == nullptr) return;
    if(api->cfg_get_int && api->cfg_get_int("EventStarter","enabled",1)==0) {
        api->log("EventStarter: optional summon testing disabled");return;
    }
    if (api->abi_version < 6 || api->install_hook == nullptr) {
        api->log("EventStarter: requires ED9Loader ABI v6");
        return;
    }
    g_api = api;

    if (api->cfg_get_int != nullptr) {
        g_rva_spawn = static_cast<unsigned>(api->cfg_get_int("EventStarter", "rva_spawn", static_cast<int>(g_rva_spawn)));
        g_rva_scrmgr = static_cast<unsigned>(api->cfg_get_int("EventStarter", "rva_scrmgr", static_cast<int>(g_rva_scrmgr)));
        g_rva_frame = static_cast<unsigned>(api->cfg_get_int("EventStarter", "rva_frame", static_cast<int>(g_rva_frame)));
        g_hotkey_vk = static_cast<unsigned>(api->cfg_get_int("EventStarter", "hotkey_vk", static_cast<int>(g_hotkey_vk)));
        g_toggle_vk = static_cast<unsigned>(api->cfg_get_int("EventStarter", "toggle_vk", static_cast<int>(g_toggle_vk)));
        const int mode = api->cfg_get_int("EventStarter", "model_mode", 1);
        InterlockedExchange(&g_model_mode, mode != 0 ? 1 : 0);
        const int character = api->cfg_get_int("EventStarter", "character", 0);
        InterlockedExchange(&g_character, character < 0 || character > 3 ? 0 : character);
        if (api->cfg_set_int != nullptr)
            api->cfg_set_int("EventStarter", "hotkey_vk", static_cast<int>(g_hotkey_vk));
    }

    const uintptr_t base = Base();
    g_spawn = reinterpret_cast<FnSpawn>(base + g_rva_spawn);
    void* target = reinterpret_cast<void*>(base + g_rva_frame);
    char address_msg[240] = {};
    _snprintf_s(address_msg, sizeof(address_msg), _TRUNCATE,
                "EventStarter: base=0x%llx spawn=0x%llx scrmgr=0x%llx frame=0x%llx vk=%u",
                static_cast<unsigned long long>(base),
                static_cast<unsigned long long>(base + g_rva_spawn),
                static_cast<unsigned long long>(base + g_rva_scrmgr),
                static_cast<unsigned long long>(base + g_rva_frame),
                g_hotkey_vk);
    api->log(address_msg);
    if (api->install_hook(target, reinterpret_cast<void*>(&Frame_Detour),
                          reinterpret_cast<void**>(&g_frame_orig)) != 0) {
        api->log("EventStarter: failed to hook field frame; hotkey disabled");
        return;
    }

    if (api->register_command != nullptr) {
        api->register_command("-scherazard", "summon Scherazard using the selected model (default F8)", Cmd_Scherazard);
        api->register_command("-scherazard-mode", "switch original/edited Scherazard model", Cmd_ModelMode);
        api->register_command("-scherazard-character", "select Scherazard, Estelle or Joshua for the summon HUD", Cmd_Character);
        api->register_command("-event", "start a loaded script event by name", Cmd_Scherazard);
    }

    char msg[160] = {};
    _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                "EventStarter: F8 summon enabled (toggle_vk=%u, character=%s, event=%s)",
                g_toggle_vk,
                CharacterName(InterlockedCompareExchange(&g_character, 0, 0)),
                SelectedEventName());
    api->log(msg);
    if (InterlockedCompareExchange(&g_observer_started, 1, 0) == 0) {
        HANDLE observer = CreateThread(nullptr, 0, ObserveInput, nullptr, 0, nullptr);
        if (observer) { CloseHandle(observer); api->log("EventStarter diagnostic observer enabled (read-only input monitoring)"); }
        else api->log("EventStarter diagnostic observer failed to start");
    }
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
