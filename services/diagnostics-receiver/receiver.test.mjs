import { test } from "node:test";
import assert from "node:assert/strict";
import { randomUUID, webcrypto } from "node:crypto";
import { readReport, validateReport, fingerprint, ReportReceiver, GitHubIssues } from "./receiver.mjs";
import worker, { Reports } from "./worker.mjs";

globalThis.crypto ??= webcrypto;
const report = () => ({ schema: 1, eventId: randomUUID().replaceAll("-", ""), sessionId: randomUUID().replaceAll("-", ""),
  createdUtc: new Date().toISOString(), version: "2026.1.0-dev", platform: "Windows", osVersion: "10.0.22631", architecture: "X64",
  runtimeVersion: "10.0.0", component: "Desktop", operation: "OrderRead", severity: "Error", writeFailure: null,
  exceptions: [{ type: "System.IO.IOException", hResult: -2146232800, frames: ["Jornal.Infrastructure.Access.Reader.Read"], nativeCode: null }],
  recent: [{ operation: "OrderRead", at: new Date().toISOString() }] });
const request = r => new Request("https://receiver.test/v1/reports", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(r) });
class Storage {
  data = new Map();
  async get(key) { return structuredClone(this.data.get(key)); }
  async put(key, value) { this.data.set(key, structuredClone(value)); }
  async delete(key) { this.data.delete(key); }
  async list({ limit, startAfter }) { return new Map([...this.data].sort(([a], [b]) => a.localeCompare(b)).filter(([key]) => !startAfter || key > startAfter).slice(0, limit)); }
}
class GitHub {
  isPrivate = true; issues = []; writes = 0; failAfterCreate = false;
  async ensurePrivate() { if (!this.isPrivate) throw new Error("public"); }
  async find(fp) { return this.issues.find(item => item.body.includes(`journal-report:${fp}`))?.number ?? null; }
  async create(title, body) {
    const issue = { number: this.issues.length + 1, title, body }; this.issues.push(issue); this.writes++;
    if (this.failAfterCreate) throw new Error("response lost");
    return issue;
  }
  async update(number, body, title) { const issue = this.issues.find(item => item.number === number); issue.body = body; if (title) issue.title = title; this.writes++; }
}

test("accepts only allowlisted, bounded technical fields", async () => {
  assert.deepEqual(await readReport(request(report())).then(r => r.version), "2026.1.0-dev");
  const invalid = [
    r => { r.message = "SECRET-CUSTOMER"; },
    r => { r.exceptions[0].message = "SECRET-DOCUMENT"; },
    r => { r.exceptions[0].frames = ["C:\\shared\\OrderDB.mdb"]; },
    r => { r.version = "secret@example.org"; },
    r => { r.recent[0].path = "SECRET-PATH"; },
    r => { r.exceptions = Array(5).fill(r.exceptions[0]); },
    r => { r.createdUtc = new Date(Date.now() - 15 * 86400000).toISOString(); },
    r => { r.operation = "arbitrary text"; }
  ];
  for (const mutate of invalid) { const r = report(); mutate(r); await assert.rejects(readReport(request(r))); }
  await assert.rejects(readReport(new Request("https://receiver.test", { method: "POST", headers: { "Content-Type": "application/json" }, body: "x".repeat(32769) })));
});

test("duplicate event is acknowledged once and equal errors share one issue", async () => {
  const storage = new Storage(), github = new GitHub(), receiver = new ReportReceiver(storage, github), first = report();
  const accepted = await receiver.accept(first, "client");
  assert.equal(accepted.status, 200);
  assert.deepEqual(await accepted.json(), { schema: 1, eventId: first.eventId, accepted: true });
  assert.equal((await receiver.accept(first, "client")).status, 200);
  assert.equal(github.writes, 1);
  const next = report(); next.version = "2026.1.1";
  assert.equal((await receiver.accept(next, "client")).status, 200);
  assert.equal(github.issues.length, 1);
  assert.match(github.issues[0].body, /Повторений: 2/);
  assert.match(github.issues[0].body, /2026.1.0-dev, 2026.1.1/);
  assert.equal(await fingerprint(first), await fingerprint(next));
  assert.doesNotMatch(github.issues[0].body, new RegExp(first.sessionId));
});

test("optional build and stage fields accept old queues, reject arbitrary text and preserve grouping", async () => {
  const first = report(), enriched = { ...report(), buildCommit: "a".repeat(40), buildDirty: true, stage: "CommitTransaction" };
  validateReport(first); validateReport(enriched);
  assert.equal(await fingerprint(first), await fingerprint(enriched));
  const github = new GitHub(), receiver = new ReportReceiver(new Storage(), github);
  assert.equal((await receiver.accept(first, "client")).status, 200);
  assert.equal((await receiver.accept(enriched, "client")).status, 200);
  assert.equal(github.issues.length, 1);
  assert.match(github.issues[0].body, /Повторений: 2/);
  assert.match(github.issues[0].body, /Коммит последнего события: a{40}/);
  assert.match(github.issues[0].body, /Изменения после коммита: есть\nЭтап: Подтверждение транзакции \(CommitTransaction\)/);
  for (const change of [{ buildCommit: "SECRET-PATH" }, { buildCommit: 123 }, { buildDirty: "SECRET" },
    { buildDirty: 1 }, { buildCommit: null }, { stage: "SECRET-CUSTOMER" }, { stage: null }, { sourcePath: "SECRET" }]) {
    assert.throws(() => validateReport({ ...enriched, ...change }));
  }
  // Неизвестная метаинформация не превращается в вымышленный хеш/этап.
  const older = report();
  assert.equal((await receiver.accept(older, "client")).status, 200);
  assert.match(github.issues[0].body, /Коммит последнего события: неизвестен/);
  assert.match(github.issues[0].body, /Этап: Не установлен \(Unknown\)/);
});

test("event id reuse with a different operation is rejected", async () => {
  const receiver = new ReportReceiver(new Storage(), new GitHub()), first = report();
  await receiver.accept(first, "client");
  assert.equal((await receiver.accept({ ...first, operation: "OrderCreate" }, "client")).status, 409);
});

test("Russian subject and accompanying report keep technical identifiers and safe write guidance", async () => {
  const r = { ...report(), operation: "OrderCreate", component: "AccessHost", severity: "Fatal",
    stage: "CommitTransaction", writeFailure: "OutcomeUnknown" };
  const github = new GitHub(), receiver = new ReportReceiver(new Storage(), github);
  assert.equal((await receiver.accept(r, "client")).status, 200);
  assert.match(github.issues[0].title, /^\[Journal\] Создание заявки · IOException \[[a-f0-9]{12}\]$/);
  for (const expected of ["В Journal зарегистрирована техническая ошибка", "Действие: Создание заявки (OrderCreate)",
    "Компонент: Модуль MDB (AccessHost)", "Уровень: Аварийное завершение (Fatal)",
    "Этап: Подтверждение транзакции (CommitTransaction)", "Сохранение не подтверждено (OutcomeUnknown)",
    "Перед повтором записи обновите список (F5)"]) assert.ok(github.issues[0].body.includes(expected), expected);
  github.issues[0].title = "Old English title";
  assert.equal((await receiver.accept({ ...r, eventId: report().eventId }, "client")).status, 200);
  assert.match(github.issues[0].title, /Создание заявки/);
  assert.equal(github.issues.length, 1);
  assert.match(github.issues[0].body, /Повторений: 2/);
});

test("public destination fails closed before any issue is written", async () => {
  const github = new GitHub(); github.isPrivate = false;
  const result = await new ReportReceiver(new Storage(), github).accept(report(), "client");
  assert.equal(result.status, 503);
  assert.equal(github.writes, 0);
  assert.deepEqual(await result.json(), { accepted: false });
});

test("lost create response is recovered using marker and never acknowledges too early", async () => {
  const storage = new Storage(), github = new GitHub(), first = report(); github.failAfterCreate = true;
  const receiver = new ReportReceiver(storage, github);
  assert.equal((await receiver.accept(first, "client")).status, 503);
  assert.equal(await storage.get("event:" + first.eventId), undefined);
  assert.equal((await receiver.accept(first, "client")).status, 200);
  assert.equal(github.issues.length, 1);
  assert.match(github.issues[0].body, /Повторений: 1/);
});

test("per-client and global quotas include failing attempts", async () => {
  const storage = new Storage(), github = new GitHub(), receiver = new ReportReceiver(storage, github);
  github.isPrivate = false;
  for (let i = 0; i < 50; i++) assert.equal((await receiver.accept(report(), "client")).status, 503);
  const limited = await receiver.accept(report(), "client");
  assert.equal(limited.status, 429);
  assert.equal(limited.headers.get("retry-after"), "3600");
  assert.equal((await receiver.accept(report(), "different")).status, 503);
  await storage.put("rate:" + new Date().toISOString().slice(0, 10), 500);
  assert.equal((await receiver.accept(report(), "other")).status, 429);
});

test("retention removes old identifiers and counters", async () => {
  const storage = new Storage();
  await storage.put("event:old", { at: Date.now() - 31 * 86400000 });
  await storage.put("event:current", { at: Date.now() });
  await storage.put("group:old", { first: new Date(Date.now() - 31 * 86400000).toISOString() });
  await storage.put("rate:2020-01-01", 1);
  await new ReportReceiver(storage, new GitHub()).prune();
  assert.deepEqual([...storage.data.keys()], ["event:current"]);
});

test("worker needs server secrets and forwards only safe payload and hashed client key", async () => {
  assert.equal((await worker.fetch(request(report()), {})).status, 503);
  let forwarded;
  const env = { REPORT_RATE_SALT: "salt", GITHUB_APP_PRIVATE_KEY: "configured", REPORTS: {
    idFromName: () => "id", get: () => ({ fetch: async req => { forwarded = req; return new Response("ok"); } })
  } };
  const req = request(report()); req.headers.set("CF-Connecting-IP", "192.0.2.1");
  assert.equal((await worker.fetch(req, env)).status, 200);
  assert.match(forwarded.headers.get("X-Journal-Client"), /^[a-f0-9]{64}$/);
  assert.doesNotMatch(await forwarded.text(), /192.0.2.1|salt|configured/);
  const bad = report(); bad.message = "SECRET";
  assert.equal((await worker.fetch(request(bad), env)).status, 400);
});

test("GitHub App tokens are constrained to issues in one repository", async () => {
  const pair = await crypto.subtle.generateKey({ name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" }, true, ["sign", "verify"]);
  const privateKey = Buffer.from(await crypto.subtle.exportKey("pkcs8", pair.privateKey)).toString("base64");
  const calls = [];
  const github = new GitHubIssues({ GITHUB_OWNER: "owner", GITHUB_REPOSITORY: "reports", GITHUB_APP_ID: "123", GITHUB_INSTALLATION_ID: "456", GITHUB_APP_PRIVATE_KEY: `-----BEGIN PRIVATE KEY-----\n${privateKey}\n-----END PRIVATE KEY-----` }, async (url, init) => {
    calls.push({ url, init });
    return url.endsWith("access_tokens") ? Response.json({ token: "SERVER-ONLY", expires_at: new Date(Date.now() + 3600000).toISOString() }) : Response.json({ private: true, owner: { login: "owner" }, name: "reports" });
  });
  await github.ensurePrivate();
  assert.deepEqual(JSON.parse(calls[0].init.body), { repositories: ["reports"], permissions: { issues: "write" } });
  assert.equal(calls[1].url, "https://api.github.com/repos/owner/reports");
  assert.equal(calls[1].init.headers.Authorization, "Bearer SERVER-ONLY");
  assert.ok(calls.every(call => call.init.redirect === "manual"));
});

test("durable object serializes concurrent requests to prevent duplicate issues", async () => {
  const storage = new Storage(); storage.getAlarm = async () => 1;
  const object = new Reports({ storage }, {}), github = new GitHub();
  object.receiver = new ReportReceiver(storage, github);
  const first = report();
  const send = () => { const req = request(first); req.headers.set("X-Journal-Client", "a".repeat(64)); return object.fetch(req); };
  const results = await Promise.all([send(), send()]);
  assert.deepEqual(results.map(r => r.status), [200, 200]);
  assert.equal(github.issues.length, 1);
  assert.equal(github.writes, 1);
});
