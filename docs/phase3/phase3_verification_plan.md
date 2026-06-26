# Phase 3 系统验证与验收测试计划 (Verification Plan)

本验证计划旨在为 Phase 3 知识接入层提供一套自动化与手工相结合的端到端质量保障方案。系统必须依照本计划执行全面验证，确保达到生产品质。

---

## 一、 11 大核心验证板块

验收计划必须完全覆盖并通过以下 11 个核心业务板块：

### 1. 文件上传验证 (Upload Verification)
- **验证手法**：通过前端 UI 或 Postman 上传 1KB 至 10MB 范围内的 PDF, TXT, MD 文件。
- **验收标准**：
  - 上传成功，API 立即响应 `202 Accepted` 并返回 documentId。
  - 物理文件已安全存在于 GCS 对应的 `users/{userId}/documents/{documentId}/` 路径下，文件无损坏或截断。
  - 元数据在 Firestore 中建立，初始状态为 `processing`。

### 2. 文本解析验证 (Parser Verification)
- **验证手法**：后台 Cloud Tasks 任务消费触发回调，执行 PDF 文本抽取。
- **验收标准**：
  - 能够正确抽取出内含的标准中英文字符、符号。
  - 对于内含表格或混乱排版的段落，确保提取成连续、可读的纯文本，不发生段落重叠导致的乱码。
  - 无法读取、加密或损坏的 PDF 必须被捕获，不能引发服务容器挂起。

### 3. 分块验证 (Chunking Verification)
- **验证手法**：验证 Chunker 算法。
- **验收标准**：
  - 长文本被切分为多个分块（目标大小 800 字符，前后 10% 左右重叠）。
  - 每个 Chunk 必须带齐以下 Metadata：`documentId`, `chunkIndex`, `pageNumber` (PDF 格式必须有效，Txt 默认为 1), `sectionTitle` (若无法提取则为 null/empty), `charStart`, `charEnd`。
  - **段落完整性**：分块边缘必须优先保留在自然段落或句号、分号处，严禁切断单个中文字符。

### 4. 向量化计算验证 (Embedding Verification)
- **验证手法**：验证对 `gemini-embedding-001` 的调用和物理入库。
- **验收标准**：
  - 后端在发送 Embedding 请求时，必须在 Body 中**显式指定 `outputDimensionality = 768`**。
  - 大模型 API 响应成功，返回长度正好为 768 维的浮点数数组。
  - **物理入库校验**：向量数据成功存入 Firestore 对应的 chunk 节点中。**由于高级 SDK 版本限制，物理上必须通过 `RestFirestoreVectorStore` 的 REST commit 方式进行原生 VectorValue 格式持久化。**

### 5. Firestore 向量最近邻检索验证 (Vector Search Verification)
- **验证手法**：执行 **nearest-neighbor vector search** 相似度检索测试。
- **验收标准**：
  - **REST 查询机制**：根据提问向量，系统能够**通过 REST API 的 runQuery/findNearest 检索接口**，获取该用户子集合下 Top-5 最接近的 Chunks，排除了高级 SDK 路线。
  - **自动化快照测试 (Snapshot Testing)**：为了规避解析错误，开发 B7 前必须基于 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md) 中捕获的真实物理 runQuery 响应报文结构编写自动化快照测试，确保 JSON Parser 的强壮度。
  - **检索相似度过滤**：通过应用程序 appsettings 或环境变量获取可配置的余弦距离门限（初始默认设为余弦距离不超过 `0.35`，相似度得分不低于 `0.65`）。任何超过配置门限的分块必须被硬过滤拦截，绝不喂给大模型。如果所有分块全部低于门限，直接判定为未命中状态，直接向前端返回“资料中未找到足够依据回答该问题”，降级成功。
  - **检索审计日志**：验证在检索过程中，系统必须在日志中详细记录 topK 的每一个分块的实际 `distance / score` 指标，便于后续生产环境参数调优。

### 6. RAG 增强回答验证 (RAG Generating Verification)
- **验证手法**：结合历史会话与上下文 Chunks 投递给 `gemini-2.5-flash` 进行推理。
- **验收标准**：
  - 大模型回答高度精确、客观，语义流畅，且**完全局限于 Chunks 所提供的范围，没有编造事实**。
  - 提问会话历史（拉取最近 N 条历史消息）与 RAG 问答衔接无误。

### 7. 引用质量校验 (Citations Verification)
- **验证手法**：正则提取标号与 `retrievedChunks` 进行交叉碰撞。
- **验收标准**：
  - 产生带有 `[1]` 脚标的输出。
  - 返回的 API response 中包含 `citationIntegrity` 状态评估（完全合规为 `valid`，越界清理为 `invalid_cleaned`）。
  - **幻觉清洗**：若大模型输出了超出检索范围的越界标号（如一共就 3 个 chunks 却标了 `[4]`），后端必须在回答文本中强制将其抹除替换，且 `citations` 元数据中绝对将其过滤清洗掉。

### 8. 删除流程验证 (Cascading Delete Verification)
- **验证手法**：调用 `DELETE /api/v1/documents/{documentId}`。
- **验收标准**：
  - 状态标记为 `deleting`。
  - Cloud Tasks 在后台发起异步批量物理清理（当 chunks 数量较多时，采用 Limit 分批删除，每次不超过 100，不突破 Batch 500 操作上限）。
  - GCS 中的物理文件、Firestore 中的 document 元数据、以及子集合下的所有 chunks 被 100% 物理删尽。
  - `deletedChunksCount` 返回实际已删除的 chunks 数量。

### 9. 多租户权限隔离验证 (Isolation Verification)
- **验证手法**：使用用户 A 的 Authorization Bearer Token，尝试在接口请求中传入或伪造用户 B 的 `userId` 去获取、删除、检索对方的文件。
- **验收标准**：
  - 鉴权 Middleware 与物理子集合路径（`users/{userId}/...`）强制约束，用户 A 的任何操作只会在其对应的子树下运行，直接返回越权 403 错误或空列表，绝对无法越权跨用户检索。

### 10. 失败状态与容错验证 (Failure Tolerance Verification)
- **验证手法**：上传一个由于密码保护、严重损坏而无法抽取任何文本的 PDF。
- **验收标准**：
  - 后台 Ingestion Task 遇到不可解析异常时（如 PdfPig 抛出加密保护错误），安全 Catch 异常，不引发容器退出或死锁。
  - document 状态被正确置为 `failed`，并记录脱敏后的简要错误日志（如“PDF is password protected”），完整的 Stack Trace 仅输出在 GCP Cloud Logging。
  - 触发垃圾回收：把 GCS 中已上传的该测试孤儿物理物理文件彻底删除，避免无用开销。

### 11. 性能时延指标验收 (Performance KPIs)
- **验收标准**：
  - **RAG 会话响应时延 (P50/P95)**：
    - **P50 响应时间**（中位数）：< 3 秒（检索、组装、大模型生成及引用校验完成）。
    - **P95 响应时间**（尾部）：< 8 秒（排除偶然的网络抖动）。
  - **Ingestion 后台异步处理耗时**：
    - 小文件 (< 1MB)：< 15 秒（完成解析并成功入库 768d 向量）。
    - 中等文件 (1MB ~ 5MB)：< 45 秒。
    - 大文件 (5MB ~ 10MB)：< 90 秒。
  - 以上指标最终以线上压测及性能监控日志（Cloud Logging）的实际统计分布为准。
