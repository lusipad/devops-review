(function exposeShared(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.DevOpsReviewShared = api;
  }
})(globalThis, function createShared() {
  "use strict";

  const HOST_NAME = "com.lus.devops_review";
  const CONTENT_SCRIPT_ID = "devops-review-selection";
  const MAX_SELECTED_TEXT = 32000;

  function normalizeServerUrl(value) {
    const url = new URL(String(value).trim());
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
      throw new Error("Server URL must be an HTTP or HTTPS base URL without credentials, query, or fragment.");
    }

    url.pathname = url.pathname.replace(/\/+$/, "");
    return url.toString().replace(/\/$/, "");
  }

  function permissionPattern(serverUrl) {
    const url = new URL(normalizeServerUrl(serverUrl));
    return `${url.origin}/*`;
  }

  function parsePullRequestLocation(currentUrl, configuredServerUrls) {
    const page = new URL(currentUrl);
    for (const configured of configuredServerUrls || []) {
      let server;
      try {
        server = new URL(normalizeServerUrl(configured));
      } catch {
        continue;
      }

      if (page.origin !== server.origin) {
        continue;
      }

      const basePath = server.pathname.replace(/\/+$/, "");
      if (basePath && page.pathname !== basePath && !page.pathname.startsWith(`${basePath}/`)) {
        continue;
      }

      const relativePath = page.pathname.slice(basePath.length).replace(/^\/+/, "");
      const segments = relativePath.split('/').filter(Boolean).map(safeDecode);
      const gitIndex = segments.findIndex((segment) => segment.toLowerCase() === "_git");
      if (gitIndex < 2 || gitIndex + 3 >= segments.length) {
        continue;
      }

      if (segments[gitIndex + 2].toLowerCase() !== "pullrequest") {
        continue;
      }

      const pullRequestId = Number.parseInt(segments[gitIndex + 3], 10);
      if (!Number.isSafeInteger(pullRequestId) || pullRequestId <= 0) {
        continue;
      }

      const queryPath = page.searchParams.get("path") || page.searchParams.get("filePath");
      return {
        serverUrl: normalizeServerUrl(configured),
        collection: segments[gitIndex - 2],
        project: segments[gitIndex - 1],
        repository: segments[gitIndex + 1],
        pullRequestId,
        filePath: normalizeRepositoryPath(queryPath),
      };
    }

    return null;
  }

  function normalizeRepositoryPath(value) {
    if (!value) {
      return null;
    }

    const decoded = safeDecode(value).replace(/\\/g, "/");
    return decoded.startsWith('/') ? decoded : `/${decoded}`;
  }

  function findLineNumber(element) {
    const monacoLine = findMonacoLineNumber(element);
    if (monacoLine) {
      return monacoLine;
    }

    let current = element && element.nodeType === 1 ? element : element?.parentElement;
    for (let depth = 0; current && depth < 10; depth += 1, current = current.parentElement) {
      const direct = parseLineNumberElement(current);
      if (direct) {
        return direct;
      }

      const nested = current.querySelector?.(
        "[data-line-number], [data-line], [data-line-index], .repos-line-number, [aria-label*='Line']",
      );
      const nestedLine = parseLineNumberElement(nested);
      if (nestedLine) {
        return nestedLine;
      }
    }

    return null;
  }

  function findMonacoLineNumber(element) {
    const current = element && element.nodeType === 1 ? element : element?.parentElement;
    const viewLine = current?.closest?.(".view-line");
    const editor = current?.closest?.(".monaco-editor");
    const top = Number.parseFloat(viewLine?.style?.top);
    if (!viewLine || !editor || !Number.isFinite(top)) {
      return null;
    }

    const marginRows = editor.querySelectorAll?.(".margin-view-overlays > div") || [];
    for (const row of marginRows) {
      if (Math.abs(Number.parseFloat(row.style?.top) - top) > 0.5) {
        continue;
      }

      const lineNumber = Number.parseInt(row.querySelector?.(".line-numbers")?.textContent, 10);
      if (Number.isSafeInteger(lineNumber) && lineNumber > 0) {
        return lineNumber;
      }
    }

    return null;
  }

  function parseLineNumberElement(element) {
    if (!element) {
      return null;
    }

    const candidates = [
      element.getAttribute?.("data-line-number"),
      element.getAttribute?.("data-line"),
      element.getAttribute?.("data-line-index"),
      element.getAttribute?.("aria-rowindex"),
      element.getAttribute?.("aria-label"),
      element.classList?.contains("repos-line-number") ? element.textContent : null,
    ];
    for (const candidate of candidates) {
      if (!candidate) {
        continue;
      }

      const match = String(candidate).match(/(?:line\s*)?(\d+)/i);
      if (!match) {
        continue;
      }

      const line = Number.parseInt(match[1], 10);
      if (Number.isSafeInteger(line) && line > 0) {
        return line;
      }
    }

    return null;
  }

  function findFilePath(document, location) {
    if (location.filePath) {
      return location.filePath;
    }

    const selectors = [
      "[data-file-path]",
      "[data-path]",
      ".repos-file-name",
      "[aria-label^='File path']",
    ];
    for (const selector of selectors) {
      const element = document.querySelector(selector);
      const value = element?.getAttribute("data-file-path") ||
        element?.getAttribute("data-path") ||
        element?.textContent;
      if (value && value.trim()) {
        return normalizeRepositoryPath(value.trim());
      }
    }

    return null;
  }

  function buildReviewSelection(selection, document, configuredServerUrls) {
    if (!selection || selection.rangeCount === 0) {
      return null;
    }

    const selectedText = selection.toString().trim();
    if (!selectedText) {
      return null;
    }

    const location = parsePullRequestLocation(document.location.href, configuredServerUrls);
    if (!location) {
      return null;
    }

    const lineRange = findSelectionLineRange(selection);
    const filePath = findFilePath(document, location);
    if (!lineRange || !filePath) {
      return null;
    }

    return {
      ...location,
      filePath,
      startLine: lineRange.startLine,
      endLine: lineRange.endLine,
      selectedText: selectedText.slice(0, MAX_SELECTED_TEXT),
    };
  }

  function findSelectionLineRange(selection) {
    const anchorLine = findLineNumber(selection.anchorNode);
    const focusLine = findLineNumber(selection.focusNode);
    if (anchorLine && focusLine) {
      return {
        startLine: Math.min(anchorLine, focusLine),
        endLine: Math.max(anchorLine, focusLine),
      };
    }

    const anchorElement = selection.anchorNode?.nodeType === 1
      ? selection.anchorNode
      : selection.anchorNode?.parentElement;
    const editor = anchorElement?.closest?.(".monaco-editor");
    const input = editor?.querySelector?.("textarea.inputarea");
    if (!input || input.selectionStart === input.selectionEnd) {
      return null;
    }

    const startOffset = Math.min(input.selectionStart, input.selectionEnd);
    const endOffset = Math.max(input.selectionStart, input.selectionEnd);
    return {
      startLine: countLines(input.value, startOffset),
      endLine: countLines(input.value, Math.max(startOffset, endOffset - 1)),
    };
  }

  function countLines(value, offset) {
    let lines = 1;
    for (let index = 0; index < offset; index += 1) {
      if (value.charCodeAt(index) === 10) {
        lines += 1;
      }
    }

    return lines;
  }

  function buildDiagnostics({
    selection,
    connectionState,
    bridgeVersion,
    connectionMessage,
  }) {
    const hasSelection = Boolean(selection);
    const bridge = {
      id: "bridge",
      label: "本地 Bridge",
      state: "unknown",
      detail: "尚未检测",
    };
    const configuration = {
      id: "configuration",
      label: "本地配置",
      state: "unknown",
      detail: "连接 Bridge 后才能确认",
    };

    if (connectionState === "connecting") {
      bridge.state = "checking";
      bridge.detail = "正在连接 Native Messaging Host…";
      configuration.state = "checking";
      configuration.detail = "等待 Bridge 返回状态";
    } else if (connectionState === "ready") {
      bridge.state = "success";
      bridge.detail = bridgeVersion ? `已就绪 · ${bridgeVersion}` : "已就绪";
      configuration.state = "success";
      configuration.detail = "配置已加载；Azure DevOps 与 Codex 将在分析时验证";
    } else if (connectionState === "error") {
      bridge.state = "error";
      bridge.detail = connectionMessage || "无法连接本地 Bridge";
    }

    return [
      {
        id: "extension",
        label: "浏览器扩展",
        state: "success",
        detail: "侧栏已运行",
      },
      {
        id: "selection",
        label: "PR 页面与选区",
        state: hasSelection ? "success" : "attention",
        detail: hasSelection
          ? `${selection.repository} · PR ${selection.pullRequestId} · ${selection.filePath}:${selection.startLine}-${selection.endLine}`
          : "请在 PR Files 的右侧代码中选择内容",
      },
      bridge,
      configuration,
    ];
  }

  function safeDecode(value) {
    try {
      return decodeURIComponent(value);
    } catch {
      return value;
    }
  }

  return Object.freeze({
    HOST_NAME,
    CONTENT_SCRIPT_ID,
    MAX_SELECTED_TEXT,
    normalizeServerUrl,
    permissionPattern,
    parsePullRequestLocation,
    normalizeRepositoryPath,
    findLineNumber,
    findSelectionLineRange,
    buildReviewSelection,
    buildDiagnostics,
  });
});
