#!/bin/sh
# Root olarak baslar: docker.sock varsa host GID'sini uygulama kullanicisina ekler,
# ardindan yetkiyi dusurup uygulamayi calistirir (chmod 666 gerekmez).
set -eu

APP_UID="${APP_UID:-1654}"
SOCKET_PATH="${Docker__SocketPath:-/var/run/docker.sock}"

if [ -S "$SOCKET_PATH" ]; then
    SOCK_GID="$(stat -c '%g' "$SOCKET_PATH" 2>/dev/null || true)"
    if [ -n "${SOCK_GID}" ] && [ "${SOCK_GID}" != "0" ]; then
        if ! getent group "${SOCK_GID}" >/dev/null 2>&1; then
            groupadd -g "${SOCK_GID}" dockersock 2>/dev/null || true
        fi

        APP_USER="$(getent passwd "${APP_UID}" | cut -d: -f1 || true)"
        if [ -n "${APP_USER}" ]; then
            usermod -aG "${SOCK_GID}" "${APP_USER}" 2>/dev/null || true
        fi
    fi
fi

exec setpriv \
    --reuid="${APP_UID}" \
    --regid="${APP_UID}" \
    --init-groups \
    --inh-caps=-all \
    -- \
    dotnet DokployMonitor.Web.dll
