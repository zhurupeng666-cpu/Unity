import { StrictMode, useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  getIssues,
  updateStatus,
  type Issue,
  type Priority,
  type Status,
} from "./api";
import "./style.css";

const priorities: Record<Priority, string> = {
  high: "高优先级",
  medium: "中优先级",
  low: "低优先级",
};
const statuses: Record<Status, string> = {
  open: "待处理",
  in_progress: "处理中",
  resolved: "已解决",
};
const date = (v: string) =>
  new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(v));
const message = (e: unknown) =>
  e instanceof Error ? e.message : "请求失败，请稍后重试。";

function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");
  const [priority, setPriority] = useState("all");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [loadedAt, setLoadedAt] = useState("");

  async function refresh() {
    setLoading(true);
    setError("");
    setNotice("");
    try {
      setIssues(await getIssues());
      setLoadedAt(new Date().toLocaleTimeString("zh-CN"));
    } catch (e) {
      setError(message(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    let active = true;
    getIssues()
      .then((data) => {
        if (active) {
          setIssues(data);
          setLoadedAt(new Date().toLocaleTimeString("zh-CN"));
        }
      })
      .catch((e) => {
        if (active) setError(message(e));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  async function changeStatus(issue: Issue, next: Status) {
    if (next === issue.status) return;
    setSaving(issue.id);
    setError("");
    setNotice("");
    try {
      const updated = await updateStatus(issue.id, next);
      setIssues((previous) =>
        previous.map((v) => (v.id === updated.id ? updated : v)),
      );
      setNotice(`“${updated.title}”已更新为${statuses[updated.status]}。`);
    } catch (e) {
      setError(message(e));
    } finally {
      setSaving(null);
    }
  }

  const filtered = useMemo(
    () =>
      issues.filter(
        (v) =>
          (status === "all" || v.status === status) &&
          (priority === "all" || v.priority === priority) &&
          `${v.title} ${v.description}`
            .toLowerCase()
            .includes(query.trim().toLowerCase()),
      ),
    [issues, query, status, priority],
  );

  return (
    <div className="app">
      <aside className="sidebar">
        <a className="brand" href="/" aria-label="巡检台首页">
          <span className="brand-icon">＋</span> 巡检台
          <span className="brand-en">FIELDNOTE</span>
        </a>
        <div className="workspace-label">工作空间</div>
        <div className="nav-active">
          <span>▦</span> 现场问题{" "}
          <span className="nav-count">{issues.length}</span>
        </div>
        <div className="sidebar-note">
          <span className="dot" /> AR 现场巡检原型
          <p>从现场发现，到闭环解决。</p>
        </div>
      </aside>
      <main>
        <header>
          <span>
            园区巡检 <span className="slash">/</span> 问题管理
          </span>
          <span className="environment">LOCAL WORKSPACE</span>
        </header>
        <section className="page-heading">
          <div>
            <p className="eyebrow">ISSUE MANAGEMENT</p>
            <h1>现场问题</h1>
            <p className="muted">集中查看 AR 上报记录，跟进每一项现场问题。</p>
          </div>
          <button
            className="primary"
            onClick={refresh}
            disabled={loading || saving !== null}
          >
            {loading ? "正在加载…" : "↻ 刷新列表"}
          </button>
        </section>
        <section className="stats" aria-label="问题统计">
          {[
            ["全部问题", issues.length, "total"],
            [
              "待处理",
              issues.filter((v) => v.status === "open").length,
              "open",
            ],
            [
              "处理中",
              issues.filter((v) => v.status === "in_progress").length,
              "in_progress",
            ],
            [
              "已解决",
              issues.filter((v) => v.status === "resolved").length,
              "resolved",
            ],
          ].map(([label, value, key]) => (
            <div className={`stat ${key}`} key={key}>
              <span>{label}</span>
              <strong>{value}</strong>
              <div className="stat-line" />
            </div>
          ))}
        </section>
        {error && (
          <div role="alert" className="alert">
            <strong>请求未完成</strong>
            <span>{error}</span>
            <button onClick={refresh} disabled={loading || saving !== null}>
              重新加载
            </button>
          </div>
        )}
        {notice && (
          <div role="status" className="notice">
            ✓ {notice}
          </div>
        )}
        <section className="list-panel" aria-label="问题列表">
          <div className="panel-top">
            <div>
              <h2>
                问题记录 <span>{filtered.length}</span>
              </h2>
              <p>手机上报成功后，刷新即可查看最新记录。</p>
            </div>
            <span className="sync">
              {loadedAt ? `上次刷新 ${loadedAt}` : "等待获取数据"}
            </span>
          </div>
          <div className="filters">
            <label className="search">
              <span>⌕</span>
              <input
                aria-label="搜索问题"
                placeholder="搜索标题或描述…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </label>
            <select
              aria-label="筛选优先级"
              value={priority}
              onChange={(e) => setPriority(e.target.value)}
            >
              <option value="all">全部优先级</option>
              {Object.entries(priorities).map(([k, v]) => (
                <option key={k} value={k}>
                  {v}
                </option>
              ))}
            </select>
            <select
              aria-label="筛选状态"
              value={status}
              onChange={(e) => setStatus(e.target.value)}
            >
              <option value="all">全部状态</option>
              {Object.entries(statuses).map(([k, v]) => (
                <option key={k} value={k}>
                  {v}
                </option>
              ))}
            </select>
          </div>
          {loading && issues.length === 0 ? (
            <div className="empty" role="status">
              <div className="empty-icon">↻</div>
              <h3>正在获取问题记录</h3>
              <p>请稍候…</p>
            </div>
          ) : filtered.length === 0 ? (
            <div className="empty">
              <div className="empty-icon">▧</div>
              <h3>
                {error
                  ? "暂时无法获取记录"
                  : issues.length
                    ? "没有匹配的问题"
                    : "还没有现场问题"}
              </h3>
              <p>
                {error
                  ? "请检查服务连接后重新加载。"
                  : issues.length
                    ? "试试其他关键词或筛选条件。"
                    : "在手机中识别平面、放置标记并提交第一条问题。"}
              </p>
            </div>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th scope="col">问题</th>
                    <th scope="col">优先级</th>
                    <th scope="col">处理状态</th>
                    <th scope="col">上报时间</th>
                    <th scope="col">操作</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((issue) => (
                    <IssueRow
                      key={issue.id}
                      issue={issue}
                      expanded={expanded === issue.id}
                      toggle={() =>
                        setExpanded(expanded === issue.id ? null : issue.id)
                      }
                      disabled={saving !== null || loading}
                      saving={saving === issue.id}
                      change={(next) => changeStatus(issue, next)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <div className="panel-footer">
            <span>
              共 {issues.length} 条记录 · 当前显示 {filtered.length} 条
            </span>
            <span>状态修改后自动保存</span>
          </div>
        </section>
        <footer>
          <span>FIELDNOTE / 现场巡检工作台</span>
          <span>发现 · 记录 · 解决</span>
        </footer>
      </main>
    </div>
  );
}

function IssueRow({
  issue,
  expanded,
  toggle,
  disabled,
  saving,
  change,
}: {
  issue: Issue;
  expanded: boolean;
  toggle: () => void;
  disabled: boolean;
  saving: boolean;
  change: (status: Status) => void;
}) {
  return (
    <>
      <tr>
        <td>
          <button className="issue-title" onClick={toggle}>
            {issue.title}
          </button>
          <div className="issue-id">#{issue.id.slice(0, 8)}</div>
        </td>
        <td>
          <span className={`badge priority-${issue.priority}`}>
            <i />
            {priorities[issue.priority]}
          </span>
        </td>
        <td>
          <select
            className={`status-select status-${issue.status}`}
            aria-label={`${issue.title}的状态`}
            value={issue.status}
            disabled={disabled}
            onChange={(e) => change(e.target.value as Status)}
          >
            {Object.entries(statuses).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </select>
          {saving && (
            <span className="saving" role="status">
              保存中…
            </span>
          )}
        </td>
        <td className="date">{date(issue.createdAt)}</td>
        <td>
          <button
            className="detail-button"
            onClick={toggle}
            aria-expanded={expanded}
          >
            {expanded ? "收起 ↑" : "详情 ↗"}
          </button>
        </td>
      </tr>
      {expanded && (
        <tr className="detail-row">
          <td colSpan={5}>
            <div className="detail-grid">
              <div>
                <span>问题描述</span>
                <p>{issue.description || "暂无描述"}</p>
              </div>
              <div>
                <span>AR 会话坐标 · 米</span>
                <p>
                  {["x", "y", "z"]
                    .map(
                      (axis) =>
                        `${axis.toUpperCase()} ${issue.position[axis as keyof Issue["position"]].toFixed(3)}`,
                    )
                    .join(" / ")}
                </p>
              </div>
              <div>
                <span>最后更新</span>
                <p>{date(issue.updatedAt)}</p>
              </div>
            </div>
            <div className="full-id">ID {issue.id}</div>
          </td>
        </tr>
      )}
    </>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
