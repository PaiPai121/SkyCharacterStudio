// Build a small standalone ED9 script that summons the edited Scherazard model.
//
// The game exposes the common script wrappers (chr_create/chr_info/etc.) as
// functions in every scene script.  We copy only those wrappers from an
// original scene script, then append edited and original summon entry points.
// Keeping the script small makes loading it on every map cheap and avoids
// replacing any stock DAT.

#include "../vendor/ed9_dat/ed9_dat.hpp"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iostream>
#include <map>
#include <string>
#include <vector>

namespace {

constexpr uint32_t TAG_INT = 0x40000000u;
constexpr uint32_t TAG_FLOAT = 0x80000000u;

uint32_t intSlot(int32_t value) {
    return TAG_INT | (static_cast<uint32_t>(value) & 0x3fffffffu);
}

uint32_t floatSlot(float value) {
    uint32_t bits = 0;
    static_assert(sizeof(bits) == sizeof(value));
    std::memcpy(&bits, &value, sizeof(bits));
    return TAG_FLOAT | ((bits >> 2) & 0x3fffffffu);
}

ed9::Instr PushRaw(uint32_t raw, uint32_t off) {
    ed9::Instr in;
    in.op = 0x00;
    in.codeOff = off;
    in.pushSize = 4;
    in.push.raw = raw;
    return in;
}

ed9::Instr PushString(const std::string& value, uint32_t off) {
    ed9::Instr in;
    in.op = 0x00;
    in.codeOff = off;
    in.pushSize = 4;
    in.push.isStr = true;
    in.push.str = value;
    return in;
}

ed9::Instr LoadResult(uint32_t off) {
    ed9::Instr in;
    in.op = 0x09;
    in.codeOff = off;
    in.u8 = 0;
    return in;
}

ed9::Instr Retrieve(int32_t stackOffset, uint32_t off) {
    ed9::Instr in;
    in.op = 0x02;
    in.codeOff = off;
    in.i32 = stackOffset;
    return in;
}

ed9::Instr Add(uint32_t off) {
    ed9::Instr in;
    in.op = 0x10;
    in.codeOff = off;
    return in;
}

ed9::Instr Call(uint16_t functionId, uint32_t off) {
    ed9::Instr in;
    in.op = 0x0c;
    in.codeOff = off;
    in.u16 = functionId;
    return in;
}

ed9::Instr ReturnPop(uint8_t bytes, uint32_t off) {
    ed9::Instr in;
    in.op = 0x01;
    in.codeOff = off;
    in.u8 = bytes;
    return in;
}

ed9::Instr SaveResult(uint8_t slot, uint32_t off) {
    ed9::Instr in;
    in.op = 0x0a;
    in.codeOff = off;
    in.u8 = slot;
    return in;
}

ed9::Instr Exit(uint32_t off) {
    ed9::Instr in;
    in.op = 0x0d;
    in.codeOff = off;
    return in;
}

// Emit the exact bytecode shape produced by KuroTools' CallFunction helper:
// caller function id, a relocatable return address, reversed arguments, CALL.
void EmitCall(ed9::Func& fn, uint32_t callerId, uint16_t target, const std::vector<ed9::Instr>& args,
              uint32_t& off) {
    const uint32_t callOff = off;
    fn.code.push_back(PushRaw(callerId, off));
    off += ed9::instrLen(fn.code.back());

    ed9::Instr ret = PushRaw(0, off);
    ret.isRetAddr = true;
    ret.retTarget = callOff + 0; // replaced below with the CALL's code offset
    fn.code.push_back(ret);
    off += ed9::instrLen(fn.code.back());

    for (auto it = args.rbegin(); it != args.rend(); ++it) {
        ed9::Instr copy = *it;
        copy.codeOff = off;
        fn.code.push_back(copy);
        off += ed9::instrLen(fn.code.back());
    }

    fn.code.push_back(Call(target, off));
    const uint32_t actualCallOff = off;
    off += ed9::instrLen(fn.code.back());
    // The return push precedes the CALL.  ed9::assemble resolves it to
    // (new CALL address + 3), so bind it to this CALL's original code offset.
    fn.code[fn.code.size() - (args.size() + 2)].retTarget = actualCallOff;
}

uint32_t crcLikeKuroTools(const std::string& name) {
    // KuroTools writes the function-name header as the standard reflected
    // CRC32 state before the final xor (equivalent to zlib.crc32(name)^0xffffffff).
    // Compute it for both summon entry points instead of hard-coding one name.
    uint32_t crc = 0xffffffffu;
    for (unsigned char c : name) {
        crc ^= c;
        for (int bit = 0; bit < 8; ++bit)
            crc = (crc >> 1) ^ (0xedb88320u & static_cast<uint32_t>(-(int32_t)(crc & 1u)));
    }
    return crc;
}

} // namespace

int main(int argc, char** argv) {
    if (argc != 3 && argc != 4) {
        std::cerr << "usage: build_summon_dat <wrapper_source.dat> <output.dat> [chrNNNN]\n";
        return 2;
    }
    const std::string selected = argc == 4 ? argv[3] : "chr5002";
    if (selected.size()!=7 || selected.substr(0,3)!="chr" ||
        !std::all_of(selected.begin()+3,selected.end(),[](char c){return c>='0' && c<='9';})) {
        std::cerr << "invalid model id\n";return 2;
    }

    std::ifstream input(argv[1], std::ios::binary);
    if (!input) {
        std::cerr << "cannot open source DAT: " << argv[1] << "\n";
        return 1;
    }
    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(input)), {});

    ed9::Script source;
    try {
        source = ed9::parse(bytes);
    } catch (const std::exception& e) {
        std::cerr << "parse source failed: " << e.what() << "\n";
        return 1;
    }

    const std::vector<std::string> needed = {
        "EVENT_BEGIN_SEAMLESS", "EVENT_FINALIZE_SEAMLESS", "begin_async",
        "end_async", "chr_create", "chr_release", "chr_info", "chr_set_pos",
        "event_entry_chr",
    };

    std::map<std::string, uint16_t> ids;
    ed9::Script out;
    out.name = "ScherazardSummon";
    out.nScriptVarIn = 0;
    out.nScriptVarOut = 0;
    for (const auto& name : needed) {
        auto it = std::find_if(source.funcs.begin(), source.funcs.end(),
                               [&](const ed9::Func& f) { return f.name == name; });
        if (it == source.funcs.end()) {
            std::cerr << "source DAT is missing wrapper: " << name << "\n";
            return 1;
        }
        if (out.funcs.size() >= 0xffffu) {
            std::cerr << "too many functions\n";
            return 1;
        }
        ed9::Func copy = *it;
        copy.start = 0;
        copy.code.shrink_to_fit();
        ids[name] = static_cast<uint16_t>(out.funcs.size());
        out.funcs.push_back(std::move(copy));
    }

    // Use a code offset outside the copied source range so the low-level
    // assembler's relocation map has unique keys before it recomputes layout.
    uint32_t maxOff = 0;
    uint32_t maxStart = 0;
    for (const auto& f : out.funcs) {
        maxStart = std::max(maxStart, f.start);
        for (const auto& in : f.code) maxOff = std::max(maxOff, in.codeOff + ed9::instrLen(in));
    }
    uint32_t off = maxOff + 0x1000u;
    const uint32_t firstEventStart = maxStart + 0x1000u;

    // 64000 is outside the stock event/party ids found in the extracted
    // scena scripts (62000, 62320/62321 and 65000+ are already used).
    // Every selectable character uses this same temporary actor, so pressing
    // the button again cleanly replaces the previous test copy.
    const uint32_t actorId = 64000u;
    const uint32_t playerId = 65533u;

    auto buildSummon = [&](const std::string& name, const std::string& model,
                           uint32_t functionId, uint32_t start) {
        ed9::Func summon;
        summon.name = name;
        summon.crc = crcLikeKuroTools(summon.name);
        summon.nin = 0;
        summon.nout = 0;
        // Scene event functions use b0=0.  The common wrapper functions copied
        // above use b0=1, but these entries are real events launched by
        // EventStarter.
        summon.b0 = 0;
        summon.b1 = 0;
        summon.start = start;

        auto constPush = [&](uint32_t raw) {
            ed9::Instr in = PushRaw(raw, off);
            off += ed9::instrLen(in);
            return in;
        };

        // This is deliberately an in-field utility event.  EVENT_BEGIN_SEAMLESS
        // opens the cinema frame and disables field input until its matching
        // EVENT_FINALIZE_SEAMLESS.  A summon must leave the normal HUD and
        // movement loop untouched, so the two wrappers are copied above for
        // compatibility but are not called here.
        EmitCall(summon, functionId, ids["chr_release"],
                 {constPush(intSlot(static_cast<int32_t>(actorId)))}, off);
        EmitCall(summon, functionId, ids["begin_async"], {constPush(intSlot(0))}, off);
        EmitCall(summon, functionId, ids["chr_create"],
                 {constPush(intSlot(static_cast<int32_t>(actorId))),
                  PushString(model, off), PushString("", off),
                  constPush(intSlot(0))}, off);
        EmitCall(summon, functionId, ids["end_async"], {}, off);
        EmitCall(summon, functionId, ids["event_entry_chr"],
                 {constPush(intSlot(static_cast<int32_t>(actorId))), constPush(intSlot(0)),
                  constPush(intSlot(0))}, off);

        // Read the player's position and facing from chr_info.  The special
        // character id 65533 is the active player in field scripts; properties
        // 1/2/3/9 are X/Y/Z/rotation respectively.
        auto query = [&](int property) {
            EmitCall(summon, functionId, ids["chr_info"],
                     {constPush(intSlot(static_cast<int32_t>(playerId))),
                      constPush(intSlot(property))}, off);
            ed9::Instr load = LoadResult(off);
            off += ed9::instrLen(load);
            summon.code.push_back(load);
        };
        query(1); // X, stack index 0
        query(2); // Y, stack index 1
        query(3); // Z, stack index 2
        query(9); // facing, stack index 3

        auto emitAtCurrent = [&](ed9::Instr in) {
            in.codeOff = off;
            off += ed9::instrLen(in);
            summon.code.push_back(in);
        };

        // Build chr_set_pos's reversed argument stack: rotation, Z+1.5, Y,
        // X+1.5, actor id.  This call is emitted manually because each
        // arithmetic expression consists of several instructions.
        emitAtCurrent(PushRaw(functionId, off));
        const size_t posRetIndex = summon.code.size();
        ed9::Instr posRet = PushRaw(0, off);
        posRet.isRetAddr = true;
        emitAtCurrent(posRet);
        // Stack before this call is [X, Y, Z, R].  The VM's relative stack
        // indexes include the hidden caller id and return address, so these
        // offsets address the four saved results as temporaries are appended.
        emitAtCurrent(Retrieve(-12, off)); // R
        emitAtCurrent(Retrieve(-20, off)); // Z
        emitAtCurrent(PushRaw(floatSlot(1.5f), off));
        emitAtCurrent(Add(off));
        emitAtCurrent(Retrieve(-28, off)); // Y
        emitAtCurrent(Retrieve(-36, off)); // X
        emitAtCurrent(PushRaw(floatSlot(1.5f), off));
        emitAtCurrent(Add(off));
        emitAtCurrent(PushRaw(intSlot(static_cast<int32_t>(actorId)), off));
        const uint32_t posActualCallOff = off;
        emitAtCurrent(Call(ids["chr_set_pos"], off));
        summon.code[posRetIndex].retTarget = posActualCallOff;

        summon.code.push_back(ReturnPop(16, off));
        off += ed9::instrLen(summon.code.back());
        summon.code.push_back(Exit(off));
        off += ed9::instrLen(summon.code.back());
        return summon;
    };

    // Stock model stems are used for Estelle/Joshua.  Scherazard has an
    // additional alias entry point so the HUD can choose the edited or stock
    // mesh without changing the global chr5002 redirect.
    const std::vector<std::pair<std::string, std::string>> summonEntries = {
        {"ScherazardSummon", "chr5002"},
        {"ScherazardSummonOriginal", "chr5002_original"},
        {"EstelleSummon", "chr5000"},
        {"JoshuaSummon", "chr0001"},
        {"StudioSummon", selected},
        {"StudioSummonOriginal", "chr_studio_original"},
    };
    for (size_t i = 0; i < summonEntries.size(); ++i) {
        const uint32_t functionId = static_cast<uint32_t>(out.funcs.size());
        out.funcs.push_back(buildSummon(summonEntries[i].first, summonEntries[i].second,
                                         functionId, firstEventStart + static_cast<uint32_t>(i) * 0x1000u));
    }

    std::vector<uint8_t> result;
    try {
        result = ed9::assemble(out);
    } catch (const std::exception& e) {
        std::cerr << "assemble failed: " << e.what() << "\n";
        return 1;
    }
    std::ofstream output(argv[2], std::ios::binary);
    if (!output) {
        std::cerr << "cannot create output DAT: " << argv[2] << "\n";
        return 1;
    }
    output.write(reinterpret_cast<const char*>(result.data()), static_cast<std::streamsize>(result.size()));
    if (!output) {
        std::cerr << "write output DAT failed\n";
        return 1;
    }
    std::cout << "SUMMON_DAT_OK: functions=" << out.funcs.size()
              << " bytes=" << result.size() << " actor_id=" << actorId << "\n";
    return 0;
}
