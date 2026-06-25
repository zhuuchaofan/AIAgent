# Phase 2 不在范围内的特性 (Non-Goals)

为了保证 Phase 2 能够敏捷落地且可验证，我们明确在这一大阶段的主线任务中**绝对不包含**以下特性。任何尝试引入以下特性的讨论或代码实现均会被推迟至 Phase 2 Plus 或 Phase 3。

---

## 一、 基础设施与外部集成

### 1. 外部通知推送系统
*   **非目标**：不集成 Telegram Bot、不接 Server酱/PushDeer、不使用 Web Push Notifications 等任何主动触达用户的渠道。
*   **范围界定**：Phase 2 所有的提醒和总结展示，均仅限在用户主动打开的 Web Frontend Dashboard 内呈现。

### 2. 自动化定时调度与后台任务架构 (明确划归为 Phase 2 Plus)
*   **非目标**：不配置 GCP Cloud Scheduler 定时任务，不配置 Eventarc 异步队列，不使用 Firestore Trigger 编写 Cloud Functions 自动触发任务。
*   **接口限制**：**/internal/agent/jobs/daily-summary** 接口（即用于被外部 Scheduler 定期调用的自动拉取/生成接口）**明确不在 Phase 2A / 2B / 2C 范围内开发**，而是推迟到 **Phase 2 Plus** 阶段实现。
*   **当前替代方案**：所有的 Agent 任务和每日总结生成，必须仅通过专门暴露的 POST `/api/daily-summaries/generate` 用户接口，供前端界面或 Postman 手动触发以保证开发阶段可控调试。

---

## 二、 复杂查询与逻辑

### 1. 多标签复杂筛选
*   **非目标**：不实现诸如 `tag=骑行 AND tag=高心率` 或者 `tag=骑行 OR tag=跑步` 的复杂组合逻辑。
*   **范围界定**：Phase 2 仅支持最简单的单一标签精确匹配筛选（`array-contains`）。

### 2. 复杂的提醒规则与循环逻辑
*   **非目标**：
    *   不支持循环提醒（如“每天喝水”、“每周四喂药”）。尽管我们在 Reminder Firestore 数据模型中增加了必填的 `repeatRule` 字段，但目前一律硬编码强置为 `"none"`。
    *   不支持跨时区动态跟随变动。
    *   不支持共享提醒给家庭其他成员，提醒一律私人可见。

---

## 三、 产品拓展能力

### 1. RAG / 长上下文历史对话检索
*   **非目标**：不支持在 Ingest 或者查询时基于用户过去的历史对话或遥远历史事件进行检索增强生成 (RAG)。
*   **范围界定**：Phase 2 的 LLM 调用依旧是单点无状态的，除每日总结外不携带过去的历史记录。

### 2. 多模态与文件输入
*   **非目标**：不实现图片上传识别、不支持语音转文字。只接受纯文本输入。

### 3. 多人协作与家庭组
*   **非目标**：不开发好友机制、家庭共享、数据共建等社交属性。Phase 2 是纯粹个人的 LifeOS，无邀请机制与组隔离设计。

### 4. 原生移动 App
*   **非目标**：不提供 React Native、Flutter 或 iOS/Android 原生移动端实现。只保证 Web 界面在移动浏览器上的响应式适配体验。
