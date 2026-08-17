# Collection-returning tools wrap their structured content in an object, because MCP
# requires structuredContent to be a JSON object rather than a bare array.
.result[] | "\(.id)\n  \(.scope), per \(.unit), \(.setId)"
