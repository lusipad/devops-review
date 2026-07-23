const test = require("node:test");
const assert = require("node:assert/strict");
const shared = require("../shared.js");

test("parses an Azure DevOps Server pull request URL", () => {
  const result = shared.parsePullRequestLocation(
    "http://localhost:8081/tfs/DefaultCollection/Orders%20Project/_git/Orders.Api/pullrequest/1427?_a=files&path=%2Fsrc%2FOrder.cs",
    ["http://localhost:8081/tfs"],
  );

  assert.deepEqual(result, {
    serverUrl: "http://localhost:8081/tfs",
    collection: "DefaultCollection",
    project: "Orders Project",
    repository: "Orders.Api",
    pullRequestId: 1427,
    filePath: "/src/Order.cs",
  });
});

test("does not activate on an unconfigured host", () => {
  const result = shared.parsePullRequestLocation(
    "https://evil.example/tfs/DefaultCollection/Project/_git/Repo/pullrequest/1",
    ["https://devops.example/tfs"],
  );
  assert.equal(result, null);
});

test("does not confuse a server path prefix", () => {
  const result = shared.parsePullRequestLocation(
    "https://devops.example/tfs-other/DefaultCollection/Project/_git/Repo/pullrequest/1",
    ["https://devops.example/tfs"],
  );
  assert.equal(result, null);
});

test("normalizes an origin-only permission pattern", () => {
  assert.equal(
    shared.permissionPattern("https://devops.example:8443/tfs/"),
    "https://devops.example:8443/*",
  );
});

test("rejects credentials in configured server URLs", () => {
  assert.throws(
    () => shared.normalizeServerUrl("https://user:secret@devops.example/tfs"),
    /without credentials/,
  );
});

test("extracts a line number from an accessible label", () => {
  const element = {
    nodeType: 1,
    getAttribute(name) {
      return name === "aria-label" ? "Line 128" : null;
    },
    classList: { contains: () => false },
    parentElement: null,
  };
  assert.equal(shared.findLineNumber(element), 128);
});

test("extracts a line number from an Azure DevOps Monaco editor", () => {
  const marginRow = {
    style: { top: "48px" },
    querySelector: () => ({ textContent: "4" }),
  };
  const editor = { querySelectorAll: () => [marginRow] };
  const viewLine = { style: { top: "48px" } };
  const element = {
    nodeType: 1,
    closest(selector) {
      return selector === ".view-line" ? viewLine : editor;
    },
  };

  assert.equal(shared.findLineNumber(element), 4);
});

test("extracts a selected line range from the Monaco input model", () => {
  const value = "first\nsecond\nthird\nfourth\n";
  const input = {
    value,
    selectionStart: value.indexOf("second"),
    selectionEnd: value.indexOf("fourth") - 1,
  };
  const editor = { querySelector: () => input };
  const anchorNode = {
    nodeType: 1,
    closest: () => editor,
    getAttribute: () => null,
    classList: { contains: () => false },
    parentElement: null,
    querySelector: () => null,
  };

  assert.deepEqual(shared.findSelectionLineRange({ anchorNode, focusNode: anchorNode }), {
    startLine: 2,
    endLine: 3,
  });
});

test("builds a Monaco selection even when the DOM reports it as collapsed", () => {
  const value = "first\nsecond\nthird\n";
  const input = {
    value,
    selectionStart: value.indexOf("second"),
    selectionEnd: value.indexOf("third") - 1,
  };
  const editor = { querySelector: () => input };
  const anchorNode = {
    nodeType: 1,
    closest: () => editor,
    getAttribute: () => null,
    classList: { contains: () => false },
    parentElement: null,
    querySelector: () => null,
  };
  const selection = {
    anchorNode,
    focusNode: anchorNode,
    rangeCount: 1,
    isCollapsed: true,
    toString: () => "second",
  };
  const document = {
    location: {
      href: "http://localhost:8081/DefaultCollection/test/_git/test/pullrequest/4?_a=files&path=%2Fsrc%2Ftax.js",
    },
  };

  assert.deepEqual(shared.buildReviewSelection(selection, document, ["http://localhost:8081"]), {
    serverUrl: "http://localhost:8081",
    collection: "DefaultCollection",
    project: "test",
    repository: "test",
    pullRequestId: 4,
    filePath: "/src/tax.js",
    startLine: 2,
    endLine: 2,
    selectedText: "second",
  });
});

test("builds actionable diagnostics before code is selected", () => {
  assert.deepEqual(
    shared.buildDiagnostics({
      selection: null,
      connectionState: "connecting",
    }),
    [
      {
        id: "extension",
        label: "浏览器扩展",
        state: "success",
        detail: "侧栏已运行",
      },
      {
        id: "selection",
        label: "PR 页面与选区",
        state: "attention",
        detail: "请在 PR Files 的右侧代码中选择内容",
      },
      {
        id: "bridge",
        label: "本地 Bridge",
        state: "checking",
        detail: "正在连接 Native Messaging Host…",
      },
      {
        id: "configuration",
        label: "本地配置",
        state: "checking",
        detail: "等待 Bridge 返回状态",
      },
    ],
  );
});

test("builds ready diagnostics with the active source selection", () => {
  assert.deepEqual(
    shared.buildDiagnostics({
      selection: {
        repository: "Orders.Api",
        pullRequestId: 1427,
        filePath: "/src/Order.cs",
        startLine: 18,
        endLine: 24,
      },
      connectionState: "ready",
      bridgeVersion: "0.2.1",
    }),
    [
      {
        id: "extension",
        label: "浏览器扩展",
        state: "success",
        detail: "侧栏已运行",
      },
      {
        id: "selection",
        label: "PR 页面与选区",
        state: "success",
        detail: "Orders.Api · PR 1427 · /src/Order.cs:18-24",
      },
      {
        id: "bridge",
        label: "本地 Bridge",
        state: "success",
        detail: "已就绪 · 0.2.1",
      },
      {
        id: "configuration",
        label: "本地配置",
        state: "success",
        detail: "配置已加载；Azure DevOps 与 Codex 将在分析时验证",
      },
    ],
  );
});

test("keeps a Native Messaging failure visible in diagnostics", () => {
  const diagnostics = shared.buildDiagnostics({
    selection: null,
    connectionState: "error",
    connectionMessage: "Specified native messaging host not found.",
  });

  assert.deepEqual(diagnostics[2], {
    id: "bridge",
    label: "本地 Bridge",
    state: "error",
    detail: "Specified native messaging host not found.",
  });
  assert.deepEqual(diagnostics[3], {
    id: "configuration",
    label: "本地配置",
    state: "unknown",
    detail: "连接 Bridge 后才能确认",
  });
});
