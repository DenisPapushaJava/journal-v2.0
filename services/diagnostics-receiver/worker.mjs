import { GitHubIssues, ReportReceiver, hash, json, readReport } from "./receiver.mjs";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/health") return json(200, { schema: 1, service: "journal-diagnostics", configured: Boolean(env.REPORT_RATE_SALT && env.GITHUB_APP_PRIVATE_KEY && env.GITHUB_APP_ID && env.GITHUB_INSTALLATION_ID) });
    if (request.method !== "POST" || url.pathname !== "/v1/reports") return json(404, { accepted: false });
    if (!env.REPORT_RATE_SALT || !env.GITHUB_APP_PRIVATE_KEY) return json(503, { accepted: false }, { "X-Journal-Failure": "configuration" });
    let report;
    try { report = await readReport(request); } catch { return json(400, { accepted: false }); }
    const client = await hash(env.REPORT_RATE_SALT + ":" + (request.headers.get("CF-Connecting-IP") ?? "unknown"));
    try {
      const target = env.REPORTS.get(env.REPORTS.idFromName("journal-reports-v1"));
      return await target.fetch(new Request("https://internal/reports", { method: "POST", headers: { "Content-Type": "application/json", "X-Journal-Client": client }, body: JSON.stringify(report) }));
    } catch { return json(503, { accepted: false }, { "X-Journal-Failure": "durable-object" }); }
  }
};

export class Reports {
  constructor(ctx, env) { this.ctx = ctx; this.receiver = new ReportReceiver(ctx.storage, new GitHubIssues(env)); this.tail = Promise.resolve(); }
  async fetch(request) {
    // Последовательная обработка без 30-секундного лимита blockConcurrencyWhile на внешние API.
    const result = this.tail.then(async () => {
      try {
        const report = await readReport(request), client = request.headers.get("X-Journal-Client");
        if (!/^[a-f0-9]{64}$/.test(client ?? "")) return json(400, { accepted: false });
        const response = await this.receiver.accept(report, client);
        if (!(await this.ctx.storage.getAlarm())) await this.ctx.storage.setAlarm(Date.now() + 86400000);
        return response;
      } catch { return json(503, { accepted: false }, { "X-Journal-Failure": "report-storage" }); }
    });
    this.tail = result.catch(() => {});
    return result;
  }
  async alarm() { await this.receiver.prune(); }
}
