# "returned of matched" first, so a capped result cannot be read as a complete one.
"\(.returned) of \(.matched) matching factors",
(.factors[] | "\(.id)\n  \(.scope), per \(.unit), \(.setId)")
