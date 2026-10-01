# Документация проекта

Реализация этапов 4.1–4.2 описана в `GAME_RULES.md`. Описание в нём фиксирует состояние этих этапов.

Этап 4.3 реализован в `ProtocolMessages.cs` и проверяется `ProtocolContractTests`.

Серверный application layer этапа 4.4 описан в `SERVER_APPLICATION.md`: API, сессии, идемпотентность,
проекции, версии и явная обработка дедлайнов.

Транспорт этапа 4.5 описан в `INPROCESS_TRANSPORT.md`: сериализация, endpoints и generations,
детерминированные сетевые сбои, очередь, logging, lifecycle и адаптер к существующему API сервера.

Два независимых клиента и привязка подготовленного UI этапа 4.6 описаны в `CLIENT_RUNTIME.md`.
Heartbeat scheduling, server-side timeout processing и reconnect остаются следующими этапами `PLAN.md`.
