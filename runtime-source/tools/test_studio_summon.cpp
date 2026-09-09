#include "scherazard_event_starter.cpp"
#include <cassert>
#include <iostream>
int main() {
    SetCharacterMode(3,false);SetModelMode(1,false);
    assert(strcmp(SelectedEventName(),"StudioSummon")==0);
    EventStarter_SummonSelected();
    assert(g_pending==1 && strcmp(g_pending_name,"StudioSummon")==0);
    assert(g_pending_frames==1);
    InterlockedExchange(&g_pending,0);
    SetModelMode(0,false);EventStarter_SummonSelected();
    assert(g_pending==1 && strcmp(g_pending_name,"StudioSummonOriginal")==0);
    SetCharacterMode(0,false);assert(strcmp(SelectedEventName(),"ScherazardSummonOriginal")==0);
    SetCharacterMode(2,false);assert(strcmp(SelectedEventName(),"JoshuaSummon")==0);
    std::cout<<"PASS native event routing, original/edited modes, shared F8/HUD queue and frame delay\n";
}
