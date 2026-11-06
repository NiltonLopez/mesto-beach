# Construye una de las tres apps (menu | staff | admin).
# Uso: docker build --build-arg APP=menu --build-arg PORT=3000 -t mestobeach-menu -f frontend/Dockerfile frontend

FROM node:20-alpine AS base
RUN corepack enable

FROM base AS pruner
WORKDIR /app
RUN apk add --no-cache libc6-compat
ARG APP
COPY . .
RUN npx turbo prune "@mestobeach/${APP}" --docker

FROM base AS installer
WORKDIR /app
RUN apk add --no-cache libc6-compat
COPY --from=pruner /app/out/json/ .
RUN pnpm install --frozen-lockfile
COPY --from=pruner /app/out/full/ .
ARG APP
ARG NEXT_PUBLIC_API_URL
ARG NEXT_PUBLIC_MENU_URL
ARG NEXT_PUBLIC_TENANT_ID
ENV NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL}
ENV NEXT_PUBLIC_MENU_URL=${NEXT_PUBLIC_MENU_URL}
ENV NEXT_PUBLIC_TENANT_ID=${NEXT_PUBLIC_TENANT_ID}
RUN npx turbo run build --filter="@mestobeach/${APP}"
# staff y admin no tienen carpeta public/; el COPY del runner necesita que exista.
RUN mkdir -p "apps/${APP}/public"

FROM base AS runner
WORKDIR /app
RUN addgroup --system --gid 1001 nodejs && adduser --system --uid 1001 nextjs
ARG APP
ARG PORT=3000
ENV APP_DIR=apps/${APP}
ENV PORT=${PORT}
ENV HOSTNAME=0.0.0.0
ENV NODE_ENV=production

COPY --from=installer --chown=nextjs:nodejs /app/${APP_DIR}/.next/standalone ./
COPY --from=installer --chown=nextjs:nodejs /app/${APP_DIR}/.next/static ./${APP_DIR}/.next/static
COPY --from=installer --chown=nextjs:nodejs /app/${APP_DIR}/public ./${APP_DIR}/public

USER nextjs
EXPOSE ${PORT}
CMD node ${APP_DIR}/server.js
