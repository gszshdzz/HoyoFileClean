# Third-party notices

`Protos/Sophon.proto` adapts the build-manifest schema from
[CollapseLauncher/Hi3Helper.Sophon](https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonManifestProto.proto).
The full build schema changes only the namespace, field names and formatting.
Read-only projections omit unused fields and track scalar presence for format
identification. The patch projection uses fields 1–3 from
[SophonPatchProto.proto](https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonPatchProto.proto)
at the same commit, under the same license below.

## Hi3Helper.Sophon — MIT License

Copyright (c) 2024-2025 Collapse Launcher

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
