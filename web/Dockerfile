# syntax=docker/dockerfile:1
# The built SPA served by `vite preview` (the same server the e2e tests use, with the strict CSP header).
# The browser, not the container, calls the API and the authority, so these are the addresses the browser sees.

FROM node:24.18-alpine
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
ARG VITE_API_BASE=http://localhost:5000
ARG VITE_AUTHORITY=http://localhost:5100
ENV VITE_API_BASE=$VITE_API_BASE VITE_AUTHORITY=$VITE_AUTHORITY
RUN npm run build && chown -R node:node /web
USER node
EXPOSE 5173
CMD ["npx", "vite", "preview", "--host", "0.0.0.0"]
