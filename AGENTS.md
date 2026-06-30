# AGENTS.md

本文件为项目所有 AI 智能体（Codex、Claude Code、Antigravity 等）的主入口和执行准则。

---

## 🛠️ 工作流与提写准则

1. **提交即完成 (Commit only)**: 本项目忽略 `git push` 操作，本地完成修改并进行 `git commit` 即视为任务完成。**禁止自主执行 push操作**。
2. **禁止提交特定目录**: 严禁将 `.claude/agents/` 目录下的任何变动提交至 Git 仓库。

---

## 🧭 核心指令与技能入口

所有 Agent 在执行对应领域的任务时，**必须参考并严格遵守**以下规范真源文档：

1. **项目开发阶段评估 (Project Phase Assessment)**
   - **执行要求**: 当需要判断项目当前阶段、下一步任务、以及是否可进入 Agent 化时，**必须参考**：
     [docs/skills/lifeos-phase-assessment.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/skills/lifeos-phase-assessment.md)
   - **关键界限**:
     - 明确 Phase 3（RAG 知识库）与 Phase 4（真实 Agent 化）的界限。
     - **Release Gate / 稳定化检查（如 Phase 3.5）不属于功能开发阶段**，属于发布与加固关卡。

2. **Cloud Run 部署规程 (Cloud Run Deployment)**
   - **执行要求**: 执行任何 API 或 Web 服务部署、版本描述、URL 获取等操作时，**必须参考**：
     [docs/skills/cloud-run-deploy.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/skills/cloud-run-deploy.md)

---

## 🛑 安全红线 (Strict Prohibitions)

未经用户明确批准，严禁执行以下操作：
* ❌ **禁止修改** Cloud Run 的环境变量（env）。
* ❌ **禁止开启** 任何写操作开关（write flags）。
* ❌ **禁止执行** 任何针对线上 Firestore、Cloud Storage 等云端资源的真实写入操作。
