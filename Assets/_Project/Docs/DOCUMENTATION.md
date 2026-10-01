# Документация проекта

Реализация этапов 4.1–4.2 описана в `GAME_RULES.md`. Описание в нём фиксирует состояние этих этапов.

Этап 4.3 реализован в `ProtocolMessages.cs` и проверяется `ProtocolContractTests`.

Серверный application layer этапа 4.4 описан в `SERVER_APPLICATION.md`: API, сессии, идемпотентность,
проекции, версии и явная обработка дедлайнов.

Транспорт этапа 4.5 описан в `INPROCESS_TRANSPORT.md`: сериализация, endpoints и generations,
детерминированные сетевые сбои, очередь, logging, lifecycle и адаптер к существующему API сервера.

Два независимых клиента и привязка подготовленного UI этапа 4.6 описаны в `CLIENT_RUNTIME.md`.
Этап 4.7 использует единый authoritative timeout-переход в `MatchService`, отменяемый серверный
UniTask loop и рассылку персональных snapshot только после фактической смены состояния. Клиентский
таймер остаётся только отображением абсолютного server deadline. Heartbeat scheduling и reconnect
остаются следующими этапами `PLAN.md`.

Этап 4.8 подключает подготовленные debug panels к endpoint-specific `NetworkSettings`, silent
disconnect/connect и фильтрации существующего transport log. `ClientDebugView` владеет четырьмя
runtime-полями debug panel и существующими сетевыми controls, а `ClientDebugController` содержит
тестируемую валидацию и преобразование процентов. Полное client recreation/resume остаётся этапом 4.9.
