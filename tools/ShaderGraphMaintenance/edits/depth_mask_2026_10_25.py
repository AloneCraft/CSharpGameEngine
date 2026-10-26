"""Keep the depth bit reconstruction explicit after the syntax-graph emitter removes redundant parentheses."""
def edit(source):
    source=source.replace("\r\n","\n")
    old="    float m = asfloat((bits & 0x007FFFFFu | 0x3F800000u));"
    assert source.count(old)==1
    return source.replace(old,"    uint mantissaBits = bits & 0x007FFFFFu;\n    float m = asfloat(mantissaBits | 0x3F800000u);")
