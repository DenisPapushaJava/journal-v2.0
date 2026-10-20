// Приёмник принимает только типизированные технические поля; сырой текст и архивы не принимаются.
const operationLabels = {
  Startup: "Запуск приложения", OrderRead: "Чтение заявок", OrderCreate: "Создание заявки", OrderEdit: "Редактирование заявки",
  OrderBatch: "Групповое изменение заявок", OrderDelete: "Удаление заявки", OrderRestore: "Восстановление заявки", Trash: "Корзина",
  PriceRead: "Чтение прайса", SpecificationImport: "Импорт спецификации", Clipboard: "Буфер обмена", Preferences: "Сохранение настроек",
  Update: "Обновление приложения", AccessHost: "Работа модуля MDB", Unexpected: "Непредвиденная ошибка"
};
const stageLabels = {
  Unknown: "Не установлен", LaunchAccessHost: "Запуск модуля MDB", SendAccessRequest: "Отправка запроса модулю MDB",
  ReadAccessResponse: "Ожидание ответа модуля MDB", ValidateAccessResponse: "Проверка ответа модуля MDB",
  ValidateAccessRequest: "Проверка запроса к модулю MDB", OpenDatabase: "Открытие базы данных", BeginTransaction: "Начало транзакции",
  ReadOrders: "Чтение заявок", ReadPrices: "Чтение прайса", CalculatePrice: "Расчёт цены", CheckSnapshot: "Проверка исходной записи",
  WriteRecord: "Запись изменений", ReadBack: "Проверка записанных данных", CommitTransaction: "Подтверждение транзакции",
  RollbackTransaction: "Откат транзакции", ReleaseResources: "Освобождение ресурсов", PrepareCuttingTime: "Подготовка хранения времени резки",
  ReadSpecification: "Чтение спецификации", ParseSpecification: "Разбор спецификации", NormalizeSpecification: "Подготовка полей заявки",
  CheckUpdates: "Проверка обновлений", DownloadUpdate: "Загрузка обновления", ApplyUpdate: "Применение обновления"
};
const operations = new Set(Object.keys(operationLabels));
const stages = new Set(Object.keys(stageLabels));
const readActions = { ReadOrders: "Загрузка списка заявок", ReadDay: "Загрузка выбранного дня", ReadOrder: "Чтение одной заявки",
  ReadMachineCatalog: "Загрузка каталога станков", ReadPriceTable: "Чтение таблицы прайса" };
const readOutcomes = { Started: "начато", Succeeded: "успешно", Failed: "ошибка", Cancelled: "отменено" };
const pathKinds = { Unknown: "не установлен", UncPath: "сетевой путь UNC", DrivePath: "путь с буквой диска (тип диска неизвестен)",
  AbsolutePath: "абсолютный путь", RelativePath: "относительный путь" };
const duration = value => Number.isInteger(value) && value >= 0 && value <= 86400000;
const symbol = /^(System\.|Jornal\.|Avalonia\.|Microsoft\.|AngleSharp\.|UglyToad\.PdfPig\.)[A-Za-z0-9_.+<>`]{1,230}$/;
const id = /^[a-f0-9]{32}$/;
const number = value => Number.isInteger(value) && value >= -2147483648 && value <= 2147483647;
const keys = (value, expected, optional = "") => value && typeof value === "object" && !Array.isArray(value) &&
  expected.split(",").every(key => Object.hasOwn(value, key)) &&
  Object.keys(value).every(key => expected.split(",").includes(key) || optional.split(",").includes(key));
const date = value => typeof value === "string" && value.length <= 40 &&
  /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));
export const json = (status, value, extra = {}) => new Response(JSON.stringify(value), {
  status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...extra }
});
export async function readReport(request) {
  if (!request.headers.get("content-type")?.startsWith("application/json")) throw new Error("format");
  if (Number(request.headers.get("content-length")) > 32768) throw new Error("size");
  const reader = request.body?.getReader();
  if (!reader) throw new Error("empty");
  const parts = []; let length = 0;
  try {
    while (true) {
      const item = await reader.read(); if (item.done) break;
      length += item.value.byteLength;
      if (length > 32768) throw new Error("size");
      parts.push(item.value);
    }
  } finally { await reader.cancel().catch(() => {}); }
  const bytes = new Uint8Array(length); let offset = 0;
  for (const part of parts) { bytes.set(part, offset); offset += part.byteLength; }
  const report = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  validateReport(report); return report;
}
export function validateReport(r) {
  if (!keys(r, "schema,eventId,sessionId,createdUtc,version,platform,osVersion,architecture,runtimeVersion,component,operation,severity,writeFailure,exceptions,recent", "buildCommit,buildDirty,stage,readContext") ||
      r.schema !== 1 || !id.test(r.eventId) || !id.test(r.sessionId) || !date(r.createdUtc) ||
      Date.parse(r.createdUtc) < Date.now() - 14 * 86400000 || Date.parse(r.createdUtc) > Date.now() + 86400000 ||
      typeof r.version !== "string" || !/^[a-zA-Z0-9.+-]{1,100}$/.test(r.version) ||
      !["Windows", "Linux", "Other"].includes(r.platform) || !/^[0-9.]{1,40}$/.test(r.osVersion) || !/^[0-9.]{1,40}$/.test(r.runtimeVersion) ||
      !["X64", "X86", "Arm64", "Arm", "Wasm", "S390x", "LoongArch64", "Armv6", "Ppc64le", "RiscV64"].includes(r.architecture) ||
      !["Desktop", "AccessHost"].includes(r.component) || !operations.has(r.operation) || !["Warning", "Error", "Fatal"].includes(r.severity) ||
      ![null, "NotSaved", "Conflict", "OutcomeUnknown"].includes(r.writeFailure) ||
      !Array.isArray(r.exceptions) || r.exceptions.length < 1 || r.exceptions.length > 4 || !Array.isArray(r.recent) || r.recent.length > 16) throw new Error("schema");
  // Необязательное расширение schema 1: старая сохранённая очередь остаётся допустимой.
  if (r.buildCommit != null && (typeof r.buildCommit !== "string" || !/^([a-f0-9]{40}|[a-f0-9]{64})$/.test(r.buildCommit)) ||
      r.buildDirty != null && (typeof r.buildDirty !== "boolean" || r.buildCommit == null) ||
      Object.hasOwn(r, "stage") && !stages.has(r.stage)) throw new Error("metadata");
  for (const item of r.exceptions) {
    if (!keys(item, "type,hResult,frames,nativeCode") || typeof item.type !== "string" || item.type.length > 240 || !symbol.test(item.type) ||
        !number(item.hResult) || item.nativeCode !== null && !number(item.nativeCode) || !Array.isArray(item.frames) || item.frames.length > 12 ||
        item.frames.some(frame => typeof frame !== "string" || frame.length > 240 || !symbol.test(frame))) throw new Error("exception");
  }
  if (r.readContext != null && (!keys(r.readContext, "action,pathKind,durationMilliseconds", "lastSuccessfulReadUtc") ||
      !Object.hasOwn(readActions, r.readContext.action) || !Object.hasOwn(pathKinds, r.readContext.pathKind) ||
      !duration(r.readContext.durationMilliseconds) || r.readContext.lastSuccessfulReadUtc != null && !date(r.readContext.lastSuccessfulReadUtc)))
    throw new Error("read-context");
  for (const item of r.recent) {
    if (!keys(item, "operation,at", "action,outcome,durationMilliseconds") || !operations.has(item.operation) || !date(item.at)) throw new Error("recent");
    const action = Object.hasOwn(item, "action") ? item.action : "Unknown";
    const outcome = Object.hasOwn(item, "outcome") ? item.outcome : "Unknown";
    if (action === "Unknown" && outcome === "Unknown" && item.durationMilliseconds == null) continue;
    if (!Object.hasOwn(readActions, action) || !Object.hasOwn(readOutcomes, outcome) ||
        (outcome === "Started" ? item.durationMilliseconds != null : !duration(item.durationMilliseconds))) throw new Error("recent-details");
  }
}
export async function hash(value) {
  const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(bytes)].map(value => value.toString(16).padStart(2, "0")).join("");
}
export async function fingerprint(r) {
  return hash([r.component, r.operation, ...r.exceptions.map(item => `${item.type}|${item.hResult}|${item.frames.join(";")}`)].join("\n"));
}
const esc = value => String(value).replaceAll("`", "'").replace(/[<>]/g, "");
const label = (labels, value) => `${labels[value]} (${value})`;
function readContextText(context) {
  if (context == null) return "";
  return `Уточнение: ${label(readActions, context.action)}\nТип пути: ${pathKinds[context.pathKind]}\n` +
    `Длительность чтения: ${context.durationMilliseconds} мс\nПоследнее успешное чтение этой БД в сеансе: ${context.lastSuccessfulReadUtc ?? "не зафиксировано"}\n`;
}
const systemErrors = { 2: "файл не найден", 3: "путь не найден", 5: "отказано в доступе", 32: "файл занят другой операцией",
  33: "участок файла заблокирован", 53: "сетевой путь не найден", 64: "сетевое имя больше недоступно", 67: "сетевое имя не найдено" };
function exceptionCodeText(item) {
  const code = item.nativeCode ?? (item.hResult >>> 16 === 0x8007 ? item.hResult & 0xffff : null);
  return `HResult ${item.hResult} (0x${(item.hResult >>> 0).toString(16).toUpperCase().padStart(8, "0")})` +
    (code !== null ? ` · Системный код ${code}${Object.hasOwn(systemErrors, code) ? " — " + systemErrors[code] : ""}` : "");
}
function breadcrumbText(item) {
  return `- ${item.at}: ${label(operationLabels, item.operation)}` +
    (Object.hasOwn(readActions, item.action) ? ` · ${readActions[item.action]}: ${readOutcomes[item.outcome]}` +
      (item.durationMilliseconds != null ? `, ${item.durationMilliseconds} мс` : "") : "");
}
function issueTitle(r, fp) {
  return `[Journal] ${operationLabels[r.operation]} · ${r.exceptions[0].type.split(".").at(-1).slice(0, 120)} [${fp.slice(0, 12)}]`;
}
function issueBody(group, r, fp) {
  return `<!-- journal-report:${fp} -->\n` +
    `В Journal зарегистрирована техническая ошибка. Ниже — автоматический отчёт для анализа.\n\n` +
    `Повторений: ${group.count}\nПервое: ${group.first}\nПоследнее: ${r.createdUtc}\n` +
    `Версии: ${group.versions.join(", ")}\nКомпонент: ${label({ Desktop: "Основное приложение", AccessHost: "Модуль MDB" }, r.component)}\nДействие: ${label(operationLabels, r.operation)}\nУровень: ${label({ Warning: "Предупреждение", Error: "Ошибка", Fatal: "Аварийное завершение" }, r.severity)}\n` +
    `Коммит последнего события: ${r.buildCommit ?? "неизвестен"}\n` +
    `Изменения после коммита: ${r.buildDirty === true ? "есть" : r.buildDirty === false ? "нет" : "неизвестно"}\nЭтап: ${label(stageLabels, r.stage ?? "Unknown")}\n` +
    readContextText(r.readContext) +
    `Результат записи: ${r.writeFailure == null ? "—" : label({ NotSaved: "Не сохранено", Conflict: "Конфликт изменений", OutcomeUnknown: "Сохранение не подтверждено" }, r.writeFailure)}\nСреда: ${r.platform} ${r.osVersion}, ${r.architecture}, .NET ${r.runtimeVersion}\n` +
    `Последний код: JR-${r.eventId.slice(0, 8).toUpperCase()}\n\n` +
    (r.writeFailure === "OutcomeUnknown" ? "Изменения могли примениться. Перед повтором записи обновите список (F5) и проверьте заявку.\n\n" : "") +
    r.exceptions.map(item => `**${esc(item.type)}** · ${exceptionCodeText(item)}\n\n\`\`\`text\n${item.frames.map(esc).join("\n")}\n\`\`\``).join("\n\n") +
    (r.recent.length ? "\n\nПоследние действия:\n" + r.recent.map(breadcrumbText).join("\n") : "");
}
class ReceiverFailure extends Error { constructor(code) { super("receiver"); this.code = code; } }

export class GitHubIssues {
  constructor(env, fetcher = globalThis.fetch.bind(globalThis)) { this.env = env; this.fetcher = fetcher; }
  async token() {
    if (this.cached && this.expires > Date.now() + 60000) return this.cached;
    this.failurePoint = "configuration";
    const app = this.env.GITHUB_APP_ID, install = this.env.GITHUB_INSTALLATION_ID;
    if (!/^\d+$/.test(app) || !/^\d+$/.test(install)) throw new ReceiverFailure("configuration");
    const b64 = bytes => btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
    const encode = value => b64(new TextEncoder().encode(JSON.stringify(value)));
    const now = Math.floor(Date.now() / 1000);
    const unsigned = encode({ alg: "RS256", typ: "JWT" }) + "." + encode({ iat: now - 30, exp: now + 300, iss: app });
    const pem = this.env.GITHUB_APP_PRIVATE_KEY?.replace(/-----[^-]+-----|\s/g, "");
    if (!pem) throw new Error("configuration");
    let signature;
    this.failurePoint = "key";
    try {
      const key = await crypto.subtle.importKey("pkcs8", Uint8Array.from(atob(pem), c => c.charCodeAt(0)),
        { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
      signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(unsigned));
    } catch { throw new ReceiverFailure("key"); }
    this.failurePoint = "token-request";
    const response = await this.fetcher(`https://api.github.com/app/installations/${install}/access_tokens`, {
      method: "POST", headers: this.headers(unsigned + "." + b64(new Uint8Array(signature))),
      body: JSON.stringify({ repositories: [this.env.GITHUB_REPOSITORY], permissions: { issues: "write" } }), signal: AbortSignal.timeout(5000), redirect: "manual"
    });
    if (!response.ok) throw new ReceiverFailure("auth-" + response.status);
    this.failurePoint = "token-response";
    const value = await response.json();
    this.cached = value.token; this.expires = Date.parse(value.expires_at); return value.token;
  }
  headers(token) { return { Authorization: "Bearer " + token, "User-Agent": "Journal-Diagnostics", Accept: "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28", "Content-Type": "application/json" }; }
  async call(path, method = "GET", body) {
    const token = await this.token();
    this.failurePoint = "repository-request";
    const response = await this.fetcher(`https://api.github.com/repos/${this.env.GITHUB_OWNER}/${this.env.GITHUB_REPOSITORY}${path}`, {
      method, headers: this.headers(token), body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(5000), redirect: "manual"
    });
    if (response.status === 401) this.cached = null;
    if (!response.ok) throw new ReceiverFailure("api-" + response.status);
    this.failurePoint = "repository-response";
    return response.json();
  }
  async ensurePrivate() {
    const repo = await this.call("");
    if (repo.private !== true || repo.owner?.login !== this.env.GITHUB_OWNER || repo.name !== this.env.GITHUB_REPOSITORY) throw new Error("private-repository-required");
  }
  async find(fp) {
    for (let page = 1; page <= 5; page++) {
      const issues = await this.call(`/issues?state=all&sort=created&direction=desc&per_page=100&page=${page}`);
      const found = issues.find(item => !item.pull_request && item.body?.startsWith(`<!-- journal-report:${fp} -->`));
      if (found) return found.number;
      if (issues.length < 100) break;
    }
    return null;
  }
  create(title, body) { return this.call("/issues", "POST", { title, body }); }
  update(number, body, title) { return this.call(`/issues/${number}`, "PATCH", { body, ...(title ? { title } : {}) }); }
}

export class ReportReceiver {
  constructor(storage, github) { this.storage = storage; this.github = github; }
  async accept(report, clientKey) {
    validateReport(report);
    const fp = await fingerprint(report), now = Date.now(), day = new Date(now).toISOString().slice(0, 10);
    const receipt = () => json(200, { schema: 1, eventId: report.eventId, accepted: true });
    const seen = await this.storage.get("event:" + report.eventId);
    if (seen) return seen.fingerprint === fp ? receipt() : json(409, { accepted: false });
    const ipKey = `rate:${day}:${clientKey}`, totalKey = "rate:" + day;
    const total = await this.storage.get(totalKey) ?? 0, rate = await this.storage.get(ipKey) ?? 0;
    if (total >= 500 || rate >= 50) return json(429, { accepted: false }, { "Retry-After": "3600" });
    // Лимитируем также неуспешные попытки, а не только доставленные отчёты.
    await this.storage.put(totalKey, total + 1); await this.storage.put(ipKey, rate + 1);
    const previous = await this.storage.get("group:" + fp);
    const group = { issue: previous?.issue ?? null, count: (previous?.count ?? 0) + 1,
      first: previous?.first ?? report.createdUtc, last: report.createdUtc,
      versions: [...new Set([...(previous?.versions ?? []), report.version])].slice(-8) };
    let stage = "github-access";
    try {
      await this.github.ensurePrivate(); // Проверка выполняется до любой записи, в том числе после смены конфигурации.
      if (!group.issue) {
        stage = "github-find";
        group.issue = await this.github.find(fp);
        if (!group.issue) {
          if (previous?.pending && now - previous.pending < 120000) return json(503, { accepted: false }, { "X-Journal-Failure": "create-pending" });
          await this.storage.put("group:" + fp, { ...previous, first: group.first, pending: now });
          stage = "github-create";
          const result = await this.github.create(issueTitle(report, fp), issueBody(group, report, fp));
          group.issue = result.number;
        } else await this.github.update(group.issue, issueBody(group, report, fp), issueTitle(report, fp));
      } else await this.github.update(group.issue, issueBody(group, report, fp), issueTitle(report, fp));
      stage = "receipt-storage";
      await this.storage.put("group:" + fp, group);
      await this.storage.put("event:" + report.eventId, { at: now, fingerprint: fp });
      return receipt();
    } catch (error) { return json(503, { accepted: false }, { "X-Journal-Failure": stage + (error instanceof ReceiverFailure ? ":" + error.code : ":" + (this.github.failurePoint ?? "unknown")) }); }
  }
  async prune() {
    const now = Date.now();
    let cursor;
    do {
      const items = await this.storage.list({ limit: 500, ...(cursor ? { startAfter: cursor } : {}) });
      for (const [key, value] of items) {
        cursor = key;
        if (key.startsWith("event:") && value.at < now - 30 * 86400000 ||
            key.startsWith("group:") && Date.parse(value.last ?? value.first ?? "") < now - 30 * 86400000 ||
            key.startsWith("rate:") && key.slice(5, 15) < new Date(now - 2 * 86400000).toISOString().slice(0, 10)) await this.storage.delete(key);
      }
      if (items.size < 500) break;
    } while (cursor);
  }
}
