# ── Stage 1: 安装依赖 ──
FROM node:20-alpine AS deps
WORKDIR /app
COPY package*.json ./
RUN npm ci

# ── Stage 2: 编译构建 ──
FROM node:20-alpine AS builder
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# 客户端所需公共配置环境变量（静态硬编码进打包客户端）
ENV NEXT_PUBLIC_FIREBASE_API_KEY=AIzaSyByXo24VBE5-cAGsQ4z56DElz-JjsIwgZ4
ENV NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN=my-agent-app-a5e42.firebaseapp.com
ENV NEXT_PUBLIC_FIREBASE_PROJECT_ID=my-agent-app-a5e42

RUN npm run build

# ── Stage 3: 运行容器 ──
FROM node:20-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

# 拷贝静态资源与样式（standalone 打包默认排除此部分以缩减体积）
COPY --from=builder /app/public ./public
COPY --from=builder /app/.next/static ./.next/static

# 拷贝 standalone 生成的独立微型运行目录
COPY --from=builder /app/.next/standalone ./

EXPOSE 3000
CMD ["node", "server.js"]
