#!/bin/bash
# Interleaved A/B: A = baseline clone (4ee45ed = 66937b9 + timing instrumentation + harness), B = candidate (branch HEAD
# working tree), B1 = candidate forced to per-piece StaticBatching (static-geometry decision). Same harness, same scenario.
B=/private/tmp/claude-501/-Users-melaniehernquist-Documents-ChatGPT-op/da48ca15-bdf3-4711-bc91-29bca88e21cf/scratchpad/world-base
C=/private/tmp/claude-501/-Users-melaniehernquist-Documents-ChatGPT-op/da48ca15-bdf3-4711-bc91-29bca88e21cf/scratchpad/wt-world
for i in 1 2 3; do
  ./run.sh $B WorldProfile.Run ab-A$i --quiet-wait -worldTag ab/A$i -worldRounds 3 -worldGens 3
  ./run.sh $C WorldProfile.Run ab-B$i --quiet-wait -worldTag ab/B$i -worldRounds 3 -worldGens 3
  ./run.sh $C WorldProfile.Run ab-S$i --quiet-wait -worldTag ab/S$i -worldRounds 3 -worldGens 3 -worldStatic 1
done
./run.sh $C WorldProfile.Run ab-controls --quiet-wait -worldTag ab/controls -worldRounds 2 -worldGens 1 -worldControls
echo "AB DONE $(date '+%H:%M:%S')" >> logs/summary.txt
