def kg: . * 100 | round / 100;

"lines    : \(.lineCount)",
"scope 1  : \(.scope1.value | kg) kg",
"scope 2  : \(.scope2LocationBased.value | kg) kg   (location-based)",
"total    : \(.total.value | kg) kg CO2e   [\(.gwpSet)]"
