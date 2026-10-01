# Реализация PLAN.md §4.1–4.2

## Конфигурация

`GameConfig` является Unity `ScriptableObject` на границе конфигурации. Значения по умолчанию:

- поле 6×6;
- корабли 3, 2, 2, 1;
- ход 15 секунд;
- latency и jitter — 0 мс;
- loss rate и duplicate rate — 0;
- heartbeat interval — 1 секунда;
- heartbeat timeout — 5 секунд.

Domain не зависит от `ScriptableObject` или Unity. `GameConfig.CreateGameRulesConfig()` создаёт обычный
неизменяемый C#-объект `GameRulesConfig`, содержащий только размер поля и состав флота. Таймер и параметры
сети остаются на границе конфигурации до реализации соответствующих этапов.

В Build Settings включена одна стартовая сцена `Assets/_Project/Scenes/GameScene.unity`.

## Domain / GameRules

`Domain` содержит `Board`, `Ship`, `Cell`, `PlayerState`, `MatchState`, `Position`, `ShotResult`,
`ShotOutcome`, `GameRulesConfig` и `GameRules`. Код слоя не использует Unity API, UI, transport или
server application layer.

`GameRules` создаёт матч с двумя случайно расставленными флотами. Расстановка использует конечный
рандомизированный backtracking, не допускает выхода за границы, пересечения или соприкосновения кораблей.
Переданный `Random` позволяет детерминированно воспроизводить расстановку в тестах.

Принятый выстрел возвращает `miss`, `hit` или `sunk` и всегда передаёт ход сопернику, кроме завершающего
матч выстрела. Невалидные координаты, неизвестный игрок, выстрел не в свой ход, повторная клетка и выстрел
после завершения матча отклоняются без изменения состояния. Победитель фиксируется после потопления
последнего корабля.

Серверные часы, дедлайны, клиентские проекции, протокол и транспорт намеренно не реализованы: они относятся
к этапам 4.3 и далее.

## Зависимости сборок

- `Battleships.Domain` не имеет зависимостей и не ссылается на Unity Engine.
- `Battleships.Configuration` зависит только от `Battleships.Domain` и Unity Engine.
- `Battleships.Protocol` не имеет зависимостей и не ссылается на Unity Engine.
- `Battleships.Networking` зависит только от `Battleships.Protocol`.
- `Battleships.Server` зависит только от `Domain` и `Protocol`.
- `Battleships.Networking.Integration` зависит от `Networking`, `Protocol` и `Server`.
- `Battleships.Client` зависит от `Protocol` и `Networking`, но не от `Server` или server-side Domain state.
- `Battleships.Presentation` зависит от `Client` и `Configuration`.
- `Battleships.Rules.Tests` зависит от `Domain` и `Configuration`.

Пустые сборки будущих слоёв фиксируют допустимые направления зависимостей, но пока не содержат функциональности.

## Проверка

EditMode-тесты проверяют конфигурацию, валидную детерминированную расстановку, координаты, очередность,
повторный выстрел, `miss`, `hit`, `sunk`, смену хода и победу. Проверка не требует сети, UI или Play Mode.
