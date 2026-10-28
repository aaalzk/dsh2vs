/**
 * dsh2vs — a configuration-only DSH bundle.
 *
 * All the wiring lives in `cordis.patch.yml`: it inserts the `@deepseek-ai/dsh-mcp-client` row that
 * connects DSH to the dsh2vs bridge (http://127.0.0.1:8790/mcp). The bridge itself is a separate
 * process (.NET) that drives MSBuild and cdb, so nothing is registered in-process here.
 *
 * `apply` is exported so the package is a well-formed plugin; it intentionally does nothing.
 * Once the bridge is running, its tools show up as `mcp__vs__*`.
 */
export function apply(ctx) {
  void ctx;
}
