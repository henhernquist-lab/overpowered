#!/bin/bash
A=/private/tmp/claude-501/-Users-melaniehernquist-Documents-ChatGPT-op/da48ca15-bdf3-4711-bc91-29bca88e21cf/scratchpad/world-base2
C=/private/tmp/claude-501/-Users-melaniehernquist-Documents-ChatGPT-op/da48ca15-bdf3-4711-bc91-29bca88e21cf/scratchpad/wt-world
for i in 1 2 3; do
  ./run.sh $A WorldProfile.Run ab2-A$i --quiet-wait -worldTag ab2/A$i -worldRounds 3 -worldGens 3
  ./run.sh $C WorldProfile.Run ab2-B$i --quiet-wait -worldTag ab2/B$i -worldRounds 3 -worldGens 3
done
./run.sh $C WorldProfile.Run ab2-controls --quiet-wait -worldTag ab2/controls -worldRounds 2 -worldGens 1 -worldControls
./run.sh $C WorldVerification.Run merged-world --quiet-wait && ./run.sh $C WorldVerification.Reload merged-world-reload --quiet-wait
./sweep.sh merged CityVerification.Run CityVerification.Reload CityArtVerification.Run HumanoidVerification.Run CombatVerification.Run ModeVerification.Run ModeVerification.Reload ModeExpansionVerification.Run ModeExpansionVerification.Reload HudVerification.Run HudPhase2Verification.Run HudPhase2Verification.Reload HudPhase3Verification.Run HudPhase3Verification.Reload AudioVerification.Run MenuPresentationVerification.Run FirstPersonVerification.Run
echo "AB2 ALL DONE $(date '+%H:%M:%S')" >> logs/summary.txt
