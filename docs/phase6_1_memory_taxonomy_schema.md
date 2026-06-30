# Phase 6.1: Memory Taxonomy & Schema 架构设计说明

本文档记录了在 Phase 6.1 本地安全设计实现（Design-Safe Implementation）阶段中，关于长期记忆引擎（Memory Engine）的最小化模型设计、分类学规范、安全性校验边界以及内存仓储设计。

## 1. 记忆分类体系 (Memory Taxonomy)

记忆分类通过 `MemoryType` 枚举定义，各分类标准及场景划分如下：

| 记忆类别 (MemoryType) | 对应值 (Snake Case) | 目标描述与典型场景 |
| :--- | :--- | :--- |
| `LifeEvent` | `life_event` | 个人生活中的重大事实记录（如搬家、换工作、宠物生病）。 |
| `Preference` | `preference` | 用户的偏好选择（如喜欢无糖咖啡、不吃香菜）。 |
| `Goal` | `goal` | 用户的短期/中期/长期目标（如今年减重 5kg，学习 Rust 语言）。 |
| `Habit` | `habit` | 自动归纳或手动确认的周期性行为习惯（如每天早起跑步、每周六打扫房间）。 |
| `Relationship` | `relationship` | 社交及人际关系网络属性（如爱人、亲戚关系）。 |
| `Knowledge` | `knowledge` | 高价值的长效私有知识（如某系统部署命令、复杂理财规则）。 |
| `Project` | `project` | 复合任务体系，含有子任务和阶段状态（如筹备婚礼、写书）。 |
| `Person` | `person` | 关系人画像及特征属性（如朋友的性格特征、家人的健康偏好）。 |
| `Location` | `location` | 空间地理常用位置（如家庭住址、经常去的自习室）。 |
| `Routine` | `routine` | 固定常规模式（如工作日 9 点通勤路线）。 |
| `Constraint` | `constraint` | **核心红线安全约束与健康红线**（如对青霉素严重过敏、禁食海鲜）。 |
| `TemporaryContext` | `temporary_context` | **短期临时上下文关注快照**，具有明确时效（如正在深圳出差至本周五）。 |

## 2. 状态生命周期 (Memory Status)

长期记忆的生命周期通过 `MemoryStatus` 枚举定义：

* `pending_confirm` (待确认)：由大模型提报，展示于前端控制台等待用户点击“确认”或“拒绝”的中间缓冲态。
* `active` (活跃)：已生效，正式参与日常 Planner 决策及 Agent 检索召回。
* `archived` (已归档)：当项目完成、目标达成或偏好发生变动后执行的归档状态，不再参与日常检索，但支持深度回顾与追溯。*（Phase 6.1 不实现物理硬删除）*

---

## 3. Schema 验证与安全红线设计 (MemoryValidator)

长期记忆引擎的安全红线重于一切。通过 `MemoryValidator` 静态校验器实现以下 11 项核心准则：

1. **`UserId` 必填性**：必须由认证上下文注入，空值直接拦截。
2. **`Type` 边界校验**：必须对应 12 类合法的 `MemoryType`。
3. **`Status` 边界校验**：必须对应合法的 `MemoryStatus`。
4. **`Content` 非空校验**：内容不可为空或纯空白字符。
5. **`Confidence` 置信区间**：置信度必须介于 `[0.0, 1.0]` 之间。
6. **`Importance` 评分标准**：重要性采用 1 至 5 分级（5 为最高重要度），任何超过此范围的数值均会被拦截。
7. **时效约束 (Temporary Context)**：如果类型为 `temporary_context`，必须包含合法的未来时间 `ExpiresAt`。
8. **时效豁免 (Non-Temporary Context)**：非 temporary_context 默认不要求有过期时间。
9. **红线升级 (Constraint Promotion)**：如果类别为 `constraint`（红线安全约束），其重要度必须默认为最高级别 5，否则校验抛出异常，防止红线重要级被漏判或低估。
10. **敏感元数据过滤 (Forbidden Metadata Keys)**：禁止任何包含以下敏感词的键（Key，不区分大小写）进入 `Metadata`：
    * `password`, `token`, `apiKey`, `secret`, `authorization`, `credential`
11. **防止 Dump 原始数据 (Raw Payload Prevention)**：
    * 序列化后的 `Metadata` JSON 块限制在 2000 字符内。
    * 键名中禁止出现名为 `payload` 或 `raw` 的键，强制大模型必须抽取结构化属性，杜绝将大量原始文本作为 Metadata 存储。
12. **内容明文凭证过滤 (Content credential filtering)**：
    * **保守安全红线规则**：如果 Content 中包含匹配 JWT (`eyJ...`) 或 Bearer 身份凭证的特征值，直接触发拒绝。此为 Phase 6.1 的高强度安全屏障，非最终复杂 NLP 分析。

---

## 4. 内存仓储物理隔离设计 (InMemoryMemoryRepository)

为了在本地进行安全的验证，仓储设计有以下特点：

- **双层隔离字典设计**：
  在 `InMemoryMemoryRepository` 内部，使用 `ConcurrentDictionary<string, ConcurrentDictionary<string, Memory>>` 组织结构。
  - 外层字典 Key 强制为 `userId`；
  - 内层字典 Key 为 `memoryId`；
  从物理结构上强制进行多租户数据隔离，绝对杜绝由于 SQL 过滤遗漏或查询参数错误带来的“越权读取”隐患。
- **只读与不可变字段**：
  在 `UpdateAsync` 过程中，仅能修改业务允许的属性，强制阻止对 `UserId`、`CreatedAt` 的二次篡改。
- **Id 前缀规则**：
  在 `CreateAsync` 创建记忆时，自动为 Id 加上 `mem_` 前缀，以明确区别于 Timeline 中 LifeEvent 的 `evt_` 标识，避免大模型与系统内部产生语义混淆。
- **不关联运行环境**：
  本仓储不在项目的 DI（Program.cs）中注册，不与 API endpoint 和 AgentRunner 打通，保持 fake-only 的独立性。

---

## 5. 单元测试设计与覆盖

单元测试通过 XUnit 框架实现，包含：
- **`MemoryModelTest`**：覆盖 12 种类型的小写下划线互转与合法性测试。
- **`MemoryValidatorTest`**：覆盖全部 11 项校验异常分支，包括长 payload、敏感 token 明文和 constraint 强制满分等场景。
- **`InMemoryMemoryRepositoryTest`**：模拟多用户并发创建，验证跨用户读取返回 null，以及篡改 UserId/CreatedAt 失败等边界安全防御。
