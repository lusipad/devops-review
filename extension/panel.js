(function initializePanel() {
  "use strict";

  const shared = globalThis.DevOpsReviewShared;
  const elements = {
    connection: document.querySelector("#connection"),
    diagnostics: document.querySelector("#diagnostics"),
    retryConnection: document.querySelector("#retry-connection"),
    openSettings: document.querySelector("#open-settings"),
    emptySelection: document.querySelector("#empty-selection"),
    details: document.querySelector("#selection-details"),
    repository: document.querySelector("#repository"),
    pullRequest: document.querySelector("#pull-request"),
    filePath: document.querySelector("#file-path"),
    lineRange: document.querySelector("#line-range"),
    selectedCode: document.querySelector("#selected-code"),
    form: document.querySelector("#review-form"),
    question: document.querySelector("#question"),
    ask: document.querySelector("#ask"),
    cancel: document.querySelector("#cancel"),
    publish: document.querySelector("#publish"),
    progress: document.querySelector("#progress"),
    answer: document.querySelector("#answer"),
    error: document.querySelector("#error"),
  };

  let selection = null;
  let port = null;
  let activeRequestId = null;
  let completedRequestId = null;
  let publishRequestId = null;
  let connectionState = "connecting";
  let connectionMessage = "";
  let bridgeVersion = null;
  let connectionGeneration = 0;

  void initialize();

  async function initialize() {
    const stored = await chrome.storage.session.get("activeSelection");
    selection = stored.activeSelection || null;
    renderSelection();
    renderDiagnostics();
    connectHost();

    chrome.storage.onChanged.addListener((changes, area) => {
      if (area === "session" && changes.activeSelection) {
        selection = changes.activeSelection.newValue || null;
        renderSelection();
        renderDiagnostics();
      }
    });

    elements.form.addEventListener("submit", startReview);
    elements.cancel.addEventListener("click", cancelReview);
    elements.publish.addEventListener("click", publishReview);
    elements.retryConnection.addEventListener("click", () => {
      if (!activeRequestId) {
        connectHost();
      }
    });
    elements.openSettings.addEventListener("click", () => void chrome.runtime.openOptionsPage());
  }

  function connectHost() {
    const generation = ++connectionGeneration;
    const previousPort = port;
    port = null;
    previousPort?.disconnect();
    connectionState = "connecting";
    connectionMessage = "";
    bridgeVersion = null;
    elements.connection.textContent = "正在连接本地 Bridge…";
    elements.connection.dataset.state = "checking";
    elements.ask.disabled = true;
    renderDiagnostics();

    try {
      const nextPort = chrome.runtime.connectNative(shared.HOST_NAME);
      port = nextPort;
      nextPort.onMessage.addListener((message) => {
        if (generation === connectionGeneration) {
          handleHostMessage(message);
        }
      });
      nextPort.onDisconnect.addListener(() => {
        if (generation !== connectionGeneration) {
          return;
        }
        const message = chrome.runtime.lastError?.message || "本地 Bridge 已断开。";
        elements.connection.textContent = message;
        elements.connection.dataset.state = "error";
        port = null;
        connectionState = "error";
        connectionMessage = message;
        bridgeVersion = null;
        finishRequest();
        renderDiagnostics();
      });

      nextPort.postMessage({ type: "host.status", requestId: crypto.randomUUID(), payload: {} });
    } catch (error) {
      const message = `无法连接本地 Bridge：${error.message}`;
      elements.connection.textContent = message;
      elements.connection.dataset.state = "error";
      connectionState = "error";
      connectionMessage = message;
      renderDiagnostics();
    }
  }

  function renderDiagnostics() {
    const stateLabels = {
      success: "正常",
      attention: "需操作",
      checking: "检测中",
      error: "错误",
      unknown: "待确认",
    };
    const diagnostics = shared.buildDiagnostics({
      selection,
      connectionState,
      bridgeVersion,
      connectionMessage,
    });

    const items = diagnostics.map((diagnostic) => {
      const item = document.createElement("li");
      item.className = "diagnostic-item";
      item.dataset.state = diagnostic.state;

      const dot = document.createElement("span");
      dot.className = "diagnostic-dot";
      dot.setAttribute("aria-hidden", "true");

      const copy = document.createElement("span");
      copy.className = "diagnostic-copy";
      const label = document.createElement("strong");
      label.className = "diagnostic-label";
      label.textContent = diagnostic.label;
      const detail = document.createElement("span");
      detail.className = "diagnostic-detail";
      detail.textContent = diagnostic.detail;
      copy.append(label, detail);

      const state = document.createElement("span");
      state.className = "diagnostic-state";
      state.textContent = stateLabels[diagnostic.state];

      item.append(dot, copy, state);
      return item;
    });
    elements.diagnostics.replaceChildren(...items);
    elements.retryConnection.disabled = Boolean(activeRequestId);
  }

  function renderSelection() {
    const hasSelection = Boolean(selection);
    elements.emptySelection.hidden = hasSelection;
    elements.details.hidden = !hasSelection;
    elements.selectedCode.hidden = !hasSelection;
    elements.ask.disabled = !hasSelection || !port;
    if (!hasSelection) {
      return;
    }

    elements.repository.textContent = selection.repository;
    elements.pullRequest.textContent = String(selection.pullRequestId);
    elements.filePath.textContent = selection.filePath;
    elements.lineRange.textContent = `${selection.startLine}-${selection.endLine}`;
    elements.selectedCode.textContent = selection.selectedText;
  }

  function startReview(event) {
    event.preventDefault();
    if (!selection || !port || activeRequestId) {
      return;
    }

    activeRequestId = crypto.randomUUID();
    completedRequestId = null;
    elements.publish.hidden = true;
    elements.answer.textContent = "";
    elements.error.hidden = true;
    elements.progress.textContent = "正在提交…";
    elements.ask.disabled = true;
    elements.cancel.hidden = false;
    renderDiagnostics();

    port.postMessage({
      type: "review.start",
      requestId: activeRequestId,
      payload: {
        ...selection,
        question: elements.question.value.trim(),
      },
    });
  }

  function publishReview() {
    if (!port || !completedRequestId || publishRequestId) {
      return;
    }
    publishRequestId = crypto.randomUUID();
    elements.publish.disabled = true;
    elements.progress.textContent = "正在发布到 PR…";
    port.postMessage({
      type: "review.publish",
      requestId: publishRequestId,
      payload: { targetRequestId: completedRequestId },
    });
  }

  function cancelReview() {
    if (!port || !activeRequestId) {
      return;
    }
    port.postMessage({
      type: "review.cancel",
      requestId: crypto.randomUUID(),
      payload: { targetRequestId: activeRequestId },
    });
    elements.progress.textContent = "正在取消…";
  }

  function handleHostMessage(message) {
    if (publishRequestId && message?.requestId === publishRequestId) {
      if (message.type === "review.published") {
        elements.progress.textContent = message.payload?.isInline ? "已发布为行内评论" : "已发布为 PR 评论";
        elements.publish.hidden = true;
      } else if (message.type === "review.failed") {
        elements.error.textContent = message.payload?.message || "发布失败。";
        elements.error.hidden = false;
        elements.publish.disabled = false;
      }
      publishRequestId = null;
      return;
    }

    if (!message || message.requestId !== activeRequestId) {
      if (message?.type === "host.status") {
        if (message.payload?.ready) {
          bridgeVersion = message.payload.version || null;
          connectionState = "ready";
          connectionMessage = "";
          elements.connection.textContent = bridgeVersion
            ? `本地 Bridge 已就绪 · ${bridgeVersion}`
            : "本地 Bridge 已就绪";
          elements.connection.dataset.state = "success";
          elements.ask.disabled = !selection;
        } else {
          connectionState = "error";
          connectionMessage = "本地 Bridge 未就绪";
          elements.connection.textContent = connectionMessage;
          elements.connection.dataset.state = "error";
          elements.ask.disabled = true;
        }
        renderDiagnostics();
      }
      return;
    }

    switch (message.type) {
      case "review.accepted":
        elements.progress.textContent = "请求已接受";
        break;
      case "review.progress":
        elements.progress.textContent = message.payload?.message || "正在分析…";
        break;
      case "review.delta":
        elements.answer.textContent += message.payload?.delta || "";
        break;
      case "review.completed":
        elements.progress.textContent = "完成";
        if (typeof message.payload?.answer === "string") {
          elements.answer.textContent = message.payload.answer;
        }
        completedRequestId = activeRequestId;
        elements.publish.hidden = false;
        elements.publish.disabled = false;
        finishRequest();
        break;
      case "review.cancelled":
        elements.progress.textContent = "已取消";
        finishRequest();
        break;
      case "review.failed":
        elements.error.textContent = message.payload?.message || "分析失败。";
        elements.error.hidden = false;
        elements.progress.textContent = "失败";
        finishRequest();
        break;
    }
  }

  function finishRequest() {
    activeRequestId = null;
    elements.ask.disabled = !selection || !port;
    elements.cancel.hidden = true;
    renderDiagnostics();
  }
})();
