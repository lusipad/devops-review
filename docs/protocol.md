# Bridge protocol

The extension and native host exchange UTF-8 JSON messages using Chrome/Edge Native Messaging framing: a four-byte little-endian payload length followed by the JSON bytes.

The native host never writes logs to stdout. A single host-to-browser message must remain below 1 MiB.

## Extension to host

```json
{
  "type": "review.start",
  "requestId": "018f...",
  "payload": {
    "serverUrl": "https://devops.example.test/tfs",
    "collection": "DefaultCollection",
    "project": "Orders",
    "repository": "Orders",
    "pullRequestId": 1427,
    "filePath": "/src/Orders/OrderService.cs",
    "startLine": 125,
    "endLine": 141,
    "selectedText": "await repository.InsertAsync(order);",
    "question": "这里并发时会不会重复创建？"
  }
}
```

The browser does not supply authoritative source or target commit values. The Bridge resolves them through Azure DevOps.

Other request types:

- `review.cancel`
- `review.publish` (references a completed `review.start` request; the browser never resends the answer)
- `host.status`

## Host to extension

- `review.accepted`
- `review.progress`
- `review.delta`
- `review.completed`
- `review.failed`
- `review.cancelled`
- `review.published`
- `host.status`

Every response includes the original `requestId`. Errors contain a stable `code` and a user-safe message; stack traces, credentials, prompts, and source contents are never sent to the browser.

`review.delta` contains only App Server messages classified as `final_answer`; interim commentary remains progress-only. `review.completed.payload.answer` contains the authoritative final text after local worktree paths have been reduced to repository-relative references. The panel replaces its streamed buffer with this value before enabling publish.
