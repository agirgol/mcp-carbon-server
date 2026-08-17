def kg: . * 100 | round / 100;

"\(.co2e.value | kg) kg CO2e   [\(.scope), \(.gwpSet)]",
"  \(.source.publisher), \(.source.publicationYear)  (\(.verification))",
(.gases[] | "  \(.gas): \(.co2e.value | kg) kg")
