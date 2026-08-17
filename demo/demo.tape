# Terminal recording for the README.
#
# Regenerate with:  vhs demo/demo.tape
#
# The tape is the source and the GIF is a build artifact. When the tool surface changes,
# edit this file and re-render — not re-record. That is the whole reason the demo is
# scripted rather than screen-captured: a hand-made recording drifts out of date silently
# and nobody notices until it is showing a command that no longer exists.
#
# Typed commands are written in backticks rather than double quotes, because VHS has no
# escape for a double quote inside a double-quoted string and every command here carries
# JSON.
#
# Needs the published tool on PATH, so that what is recorded is what a reader would
# install:
#   dotnet tool install -g McpCarbonServer --prerelease

Output demo/demo.gif

Require mcp-carbon-server
Require jq

Set Shell bash
Set FontSize 15
Set Width 1400
Set Height 620
Set Padding 24
Set TypingSpeed 25ms
Set Theme "Catppuccin Mocha"

Sleep 800ms

Type `# Factors are looked up from a compiled catalog, never recalled.`
Enter
Sleep 1200ms

Type `demo/carbon search_emission_factors '{"query":"natural gas","limit":3}' | jq -rf demo/fmt/search.jq`
Enter
Sleep 3s

Type `# Apply one. The gas split and the source come back with the number.`
Enter
Sleep 1200ms

Type `demo/carbon calculate_emissions '{"value":1000,"unit":"CubicMetre","factorId":"defra-2026/fuels/gaseous-fuels/natural-gas/cubic-metres"}' | jq -rf demo/fmt/calculate.jq`
Enter
Sleep 4s

Type `# Aggregate several activity lines into a GHG Protocol inventory.`
Enter
Sleep 1200ms

Type `demo/carbon build_inventory "$(cat demo/inventory.json)" | jq -rf demo/fmt/inventory.jq`
Enter
Sleep 4s

Type `# A unit measuring another quantity is refused rather than guessed at.`
Enter
Sleep 1200ms

Type `demo/carbon calculate_emissions '{"value":1000,"unit":"KilowattHour","factorId":"defra-2026/fuels/gaseous-fuels/natural-gas/cubic-metres"}'`
Enter
Sleep 4s
