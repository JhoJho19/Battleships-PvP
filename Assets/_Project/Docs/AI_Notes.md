# 1. AI ИНСТРУМЕНТЫ:

- MCP Coplay;
- Unity CLI;
- Codex. LLM Sol 6.1 medium - работа с проектом;
- ChatGPT Sol 5.6 hight - работа с промптами, обсуждение плана, адаптация плана для легкоусвояемости агентами;
- Opencode. LLM Grok 4.7 default - второе мнение по неоднозначным вопросом, дополнительная проверка соответствия плану хода работ.

PLAN.md основной контект. Так же агенты документировали выполнение задач в .md файлы упрощаяя себе понимание проекта в будущем.


# 2. ПРИМЕР ПРОМПТА:

Примечание: черновик промпта пишется руками, затем скармливается чату на проверку, чат дополняет промпт, я проверяю и правлю.
Затем чат переводит промпт на английский чтобы агент не тратил токены на перевод.
Для читаемости здесь я оставляю промпты на русском.

## Короткий промпт
## Решаем рутину

В папке sprites лежит UI-атлас (изображение с элементами интерфейса). Используя доступные MCP-инструменты для работы со спрайтами, выполни автоматическую нарезку по контурам (избегая фиксированной сетки, так как элементы разного размера).
Пожалуйста, учти следующие требования:
1. Нарезай только непрозрачные и полупрозрачные связанные области (изолированные UI-элементы).
2. Игнорируй и удаляй артефакты, "шум" и пустые ошибочные обводки в 1–2 пикселя.
3. Сохрани вырезанные элементы как отдельные PNG-файлы в папку sprites/output/.
Если для работы инструмента тебе нужны параметры (например, порог прозрачности/threshold), подбери оптимальные для четких UI-элементов.

## Длинный промпт
## Реализация пункта 4.6 Plan.md

Перед началом работы прочитай `PLAN.md`, `AGENTS.md` и актуальную документацию проекта.
Реализуй только раздел `4.6. Два клиента и минимальный UI`.
Не переходи к `4.7` и последующим этапам.
UI для этого этапа уже подготовлен вручную в сцене. Не создавай новый UI с нуля и не меняй его визуальный дизайн. Используй существующую иерархию и существующие TextMeshPro/UI-объекты.
Перед внесением изменений изучи:
- Protocol;
- server application layer;
- `InProcessTransport`;
- текущую сцену;
- существующую UI hierarchy;
- существующие asmdef и зависимости.
Цель этапа - подключить подготовленный UI к двум независимым `BattleClient`, работающим через один общий сервер и `InProcessTransport`, чтобы на normal network можно было полностью сыграть матч.
## Важное правило существующего UI
В иерархии UI текстовые объекты намеренно имеют суффиксы:
- `Readonly` - статический текст. Код не должен изменять его содержимое;
- `Editable` - runtime-значение, которое должен обновлять код.
Не изменяй текст объектов с суффиксом `Readonly`.
Обычно `Editable` должен содержать только само значение, без повторения заголовка.
Например:
`TextMatchStatusHeaderReadonly`
содержит статический текст:
`Match status:`
а:
`TextMatchStatusEditable`
должен содержать только:
`In progress`
Аналогично:
`TextStateVersionEditable` -> `v18`
`TextTurnIdEditable` -> `12`
`TextTimerEditable` -> `00 : 15`
## Исключение - ImageServerStatusPanel
У `ImageServerStatusPanel` строки состояния устроены иначе.
Для следующих runtime-текстов нужно выводить одновременно и название поля, и значение:
`Connected Clients: 2/2`
`Match Status: In Progress`
`State Version: v18`
`Current Turn: Player 1`
`Turn Id: 12`
То есть внутри server status panel соответствующие runtime TMP fields должны формировать полную строку `Label: Value`.
Если для `State Version` в текущей иерархии ещё нет отдельного подходящего runtime text object, добавь только необходимый TMP child, сохранив существующий стиль и структуру панели.
Не меняй статический:
`TextServerHeaderReadonly`.
## Существующая структура

В сцене уже есть:

- общий заголовок;
- `ImageServerStatusPanel`;
- два `ImagePlayerWindow`;
- отдельный `ClientDebug` внутри каждого player window;
- `RecentEvents` каждого клиента;
- общий `ImageTransportServerPanel`;
- область транспортного/server activity log.

Используй эти объекты.

Не создавай альтернативную параллельную UI hierarchy.

Не переименовывай существующие GameObject без необходимости.

Если имя какого-либо существующего элемента неоднозначно или не соответствует его визуальной роли, сначала определи его назначение по hierarchy и расположению. Не исправляй такие имена только ради красоты.

## BattleClient

Создай независимый `BattleClient` для каждого игрока.

Каждый клиент должен:

- иметь собственный endpoint `InProcessTransport`;
- отправлять все запросы только через transport API;
- получать только сообщения, адресованные этому endpoint;
- иметь собственный `ClientState`;
- не иметь прямой ссылки на `BattleServer`;
- не иметь доступа к `MatchState`;
- не иметь доступа к состоянию второго клиента;
- генерировать новый `RequestId` для новой операции выстрела;
- использовать подтверждённый `TurnId` из собственного состояния;
- хранить pending shot;
- изменять подтверждённое состояние только на основании server messages.

UI не должен содержать gameplay logic.

## ClientState

`ClientState` - это только разрешённая клиенту проекция server state.

Не сохраняй внутри него server-side `MatchState` или скрытое состояние соперника.

Раздели данные как минимум на:

- own board;
- известные результаты выстрелов по opponent board;
- match status;
- current turn;
- player identity;
- `TurnId`;
- `StateVersion`;
- server deadline;
- winner;
- pending request.

Не допускай отката подтверждённого состояния более старым `StateVersion`.

## Два клиента

В сцене одновременно работают:

- Player 1 / Client 1;
- Player 2 / Client 2.

Они должны быть полностью независимыми runtime instances.

Оба используют:

Client
-> serialized protocol message
-> InProcessTransport
-> server adapter
-> BattleServer

и тот же путь обратно.

Никаких прямых вызовов `BattleServer.Handle(...)` из `BattleClient`.

Создаётся только один общий server runtime.

## Binding существующего Player UI

Для каждого `ImagePlayerWindow` создай/используй отдельный presentation component, например `ClientView` или аналогичный по ответственности.

Он должен получать данные только от соответствующего `BattleClient` / `ClientState`.

Не связывай Player 1 UI с Player 2 runtime и наоборот.

### Header

Используй существующие header TMP objects.

`TextPlayerNumberEditable` показывает runtime player value, например:

`Player 1`

или:

`Player 2`

Объекты с `Readonly` не изменяй кодом.

Если `Client 1` / `Client 2` уже вручную настроены в соответствующих readonly fields сцены, оставь их как есть.

### Status

Используй существующие:

- `TextCurrentTurnHeaderReadonly`;
- `TextMatchStatusHeaderReadonly`;
- `TextStateVersionHeaderReadonly`;
- `TextTurnIDHeaderReadonly`;

только как статические labels.

Runtime значения выводи через:

- `TextYourTurnEditable`;
- `TextMatchStatusEditable`;
- `TextStateVersionEditable`;
- `TextTurnIdEditable`.

Например:

`TextYourTurnEditable`:
- `Your turn`
- `Waiting`

`TextMatchStatusEditable`:
- `Waiting for players`
- `In progress`
- `Finished`

`TextStateVersionEditable`:
- `v1`
- `v2`
- ...

`TextTurnIdEditable`:
- `1`
- `2`
- ...

Не дублируй названия полей внутри этих editable values.

## Turn Timer

`TextTimerEditable` должен показывать оставшееся визуальное время, например:

`00 : 15`

Countdown рассчитывается относительно server-side deadline из подтверждённого состояния.

Этот timer является только отображением.

Client/UI не должен самостоятельно:

- переключать ход;
- завершать ход;
- изменять server state;
- считать timeout authoritative.

Если фактическая server-side timeout processing относится к следующему этапу, не переносить её в `4.6`.

## Pending Request

Используй существующую область `Pending Request`.

`TextPendingRequest` используется для runtime-содержимого текущей pending operation.

Когда pending operation отсутствует, допустимо оставить поле пустым либо показать минимальное нейтральное состояние.

При отправленном выстреле можно показывать компактно, например:

`Waiting`
`Fire B6`
`Request #29`

Не превращай эту область в историю операций.

После однозначного server response pending operation должна быть завершена.

Не реализовывай retry/reconnect logic на этом этапе.

## Shot flow

При клике по opponent board:

1. Клиент выполняет только локальные проверки возможности отправки.
2. Не вычисляет `Hit`, `Miss`, `Sunk` или winner.
3. Создаёт новый `FireRequest`.
4. Использует текущий подтверждённый `TurnId`.
5. Создаёт pending operation.
6. Показывает выбранную клетку как `Pending`.
7. Блокирует второй независимый shot до разрешения текущего pending.
8. Отправляет запрос только через transport.
9. Применяет только server-confirmed result.
10. После ответа снимает `Pending`.

## Boards

Используй существующие визуальные области:

- `Own Board`;
- `Opponent Board`.

Если контейнеров для runtime cells ещё нет, добавь только необходимые child containers внутри уже существующих областей.

Не перестраивай внешний UI.

Используй переиспользуемый `CellView` prefab/component.

Сетка должна генерироваться динамически из board size.

Не создавай вручную 36 отдельных игровых cell objects в сцене.

`CellView` должен быть presentation-only.

Own Board:

- показывает собственные ships;
- показывает shots opponent;
- не принимает fire input.

Opponent Board:

- никогда не показывает hidden ships;
- показывает только подтверждённые результаты собственных shots;
- позволяет выбирать клетку для fire;
- показывает `Pending`.

Координаты A-F / 1-6 и статические подписи UI не должны пересоздаваться кодом, если они уже существуют в подготовленном интерфейсе.

## Client Debug

Используй существующий `ClientDebug` каждого игрока.

Статические labels не изменяй.

Editable fields должны показывать только runtime values.

Например:

Endpoint:
`Client A / gen 1`

Connected:
`Yes`

Last Request Id:
`#29`

Не показывай fake WebSocket URL вроде `ws://localhost:7777`, если фактически используется `InProcessTransport`.

Используй реальную logical endpoint identity существующего транспорта.

`RecentEvents` должен получать краткие клиентские runtime events, например:

`Joined match`
`Snapshot received`
`Fire B6 sent`
`Fire B6 confirmed: Hit`

Не смешивай сюда общий transport log.

## Transport / Server Activity Log

Используй существующий `ImageTransportServerPanel`.

Статический header остаётся неизменным.

Runtime log должен выводиться в существующий transport log text object.

Используй уже реализованный transport-level log из 4.5.

Не создавай второй независимый logging mechanism, если необходимые данные уже предоставляет transport.

Здесь могут отображаться события уровня:

- `SENT`;
- `RECEIVED`;
- `DROPPED`;
- `DUPLICATED`;

и server/request routing events, если они уже доступны через существующую архитектуру без нарушения границ слоёв.

На normal network в 4.6 основными будут `SENT` и `RECEIVED`.

## ClientConnectionMonitor

Реализуй только минимальный `ClientConnectionMonitor`, необходимый текущему этапу.

На 4.6 он может отражать базовое локальное состояние соединения/endpoint registration.

Не реализовывай:

- heartbeat timeout;
- silent disconnect detection;
- reconnect;
- Resume workflow;
- automatic recovery.

Это относится к последующим этапам.

## Runtime composition

Создай минимальный scene bootstrap/composition root, который связывает уже существующие компоненты:

- `BattleServer`;
- server adapter;
- `InProcessTransport`;
- endpoint Client 1;
- endpoint Client 2;
- `BattleClient` 1;
- `BattleClient` 2;
- соответствующие `ClientView`;
- server status view;
- transport log view.

Composition root может знать обо всех этих объектах для wiring.

После initialization runtime references должны оставаться направленными согласно архитектурным границам.

Не использовать Singleton.

## Server status panel

Создай presentation component для `ImageServerStatusPanel`.

Он может получать только те server-side данные, которые нужны для диагностического отображения сцены.

Он не должен использоваться клиентами для gameplay decisions.

Обновляй:

`Connected Clients: N/2`

`Match Status: ...`

`State Version: vN`

`Current Turn: Player N`

`Turn Id: N`

Это диагностическая server panel, а не часть client state.

## UI references

Используй сериализованные ссылки на существующие TMP/UI components либо небольшой view-binding component.

Не ищи элементы каждый кадр через `GameObject.Find`.

Не используй string-based hierarchy lookup в runtime, если можно установить ссылки в Inspector.

Не помещай всю UI-логику в один огромный MonoBehaviour.

Раздели как минимум ответственность между:

- client runtime;
- client state;
- player/client view;
- board view;
- cell view;
- server status view;
- transport log view;
- composition/bootstrap.

## Работа со сценой

UI уже подготовлен вручную.

Не изменяй:

- размеры;
- расположение;
- спрайты;
- цвета;
- шрифты;
- внешний дизайн;

если это не необходимо для функциональности.

При необходимости добавить component, reference или runtime container используй Unity Editor / Coplay MCP согласно `AGENTS.md`.

Не редактируй scene YAML вручную.

## Что не входит в 4.6

Не реализовывай:

- controls для delay/jitter/loss/duplication;
- silent disconnect controls;
- reconnect;
- Resume workflow;
- heartbeat monitoring;
- client recreation;
- полноценную network debug panel из 4.8;
- дополнительную server timer logic из следующих этапов;
- Mirror;
- ParrelSync;
- visual polish/redesign.

Используй normal-network settings `InProcessTransport`.

## Tests

Добавь EditMode tests для поведения, которое можно проверить без визуальной сцены:

- Client 1 и Client 2 имеют независимые `ClientState`;
- snapshot одного игрока не изменяет state второго;
- hidden opponent ship data не появляется в client state;
- один пользовательский shot создаёт один новый `FireRequest`;
- `RequestId` нового shot уникален;
- `TurnId` берётся из текущего confirmed state;
- второй независимый shot блокируется во время pending;
- соответствующий `FireResponse` разрешает pending;
- stale state не откатывает более новый `StateVersion`;
- confirmed server state корректно обновляет ClientState.

Не дублируй Domain, Server и Transport tests.

Не создавай сложную PlayMode test infrastructure только ради UI.

## Финальная интеграционная проверка

После реализации:

1. Проверь компиляцию.
2. Запусти Client/EditMode tests.
3. Запусти полный EditMode suite.
4. Запусти сцену в Play Mode для финальной интеграционной проверки.
5. Убедись, что Player 1 и Player 2 одновременно видят только разрешённое им состояние.
6. Убедись, что партия полностью проходит через `InProcessTransport`.
7. Убедись, что можно завершить полноценный матч.
8. Проверь, что Console не содержит новых ошибок.
9. Не переходи к `4.7`.

## Финальный отчёт

Кратко укажи:

- созданные и изменённые файлы;
- какие существующие UI objects были подключены;
- ответственность `BattleClient`;
- ответственность `ClientState`;
- ответственность presentation components;
- runtime composition;
- flow выстрела UI -> Client -> Transport -> Server -> Transport -> Client -> UI;
- реализацию `Pending`;
- способ обновления server status panel;
- способ обновления client debug и transport log;
- результаты EditMode tests;
- результат полноценной партии в Play Mode;
- оставшиеся ограничения, относящиеся к следующим этапам.


# 3. Где AI ошибся или предложил неудачное решение и как я это заметили.

- В предлагаемом AI плане использовался сразу Mirror (так как мы активно использовали его ранее), а хост должен был держаться у одного из клиентов, хоть и отделенный логически.
Таким образом неоднозначно выполнялось требование из ТЗ держать сервер и клиентов в одном процессе и при этом проводить тестовый реконект клиентов (хост-клиент при переподключении ронял бы сессию).
Чтобы реализовать реконект обоих игроков сохранив игровую сессию, но с реальным мультиплеерным стеком нужно делать сервер на отдельном инстансе, 
что миррор позволяет, но тогда не было бы выполнено требование ТЗ о работе в одном процессе.
Поэтому принял решение реализовать для начала минимум ТЗ с эмуляцией мультиплеера и в одном процессе.
Как заметил: проверил редакцию плана от AI.
- AI детерминировал существующий в проекте рандом расстановки кораблей. То есть не смотря на наличие логики рандомной генерации, корабли из раза в раз становились на одни и те же позиции, что можно трактовать как нарушение ТЗ.
Обнаружено при ручном тестировании. 
- AI ошибочно включил в GameConfig поля latencyMilliseconds, jitterMilliseconds, lossRate, duplicateRate значения которых из GameConfig ничем не управляют. Реальные значения latencyMilliseconds, jitterMilliseconds, lossRate, duplicateRate берутся из NetworkSettings. Эти поля не имели отношения к настройкам геймплея и поэтому они не должны были быть в GameConfig. Заметил при тесте проекта с разными конфигами.
- AI смешал ответственность закинув вывод UI игрока и вывод информации на Debug панель игрока в один класс. Заметил во время написания промпта к пункту 4.8 плана, что часть дебага у нас уже выводится в ClientView.
- AI плохо справляется с проектированием и верской игрового UI. Множество нечитаемых элементов, много дублирований выводов. Заметил когда попросил сгенерить макет UI для проекта, который бы соответствовал PLAN.md.
- При уничтожении корабля не все его клетки заменялись на спрайты sunk. Обнаружено при ручном тестировании.
- AI предлагал отложить доставку на один тик Unity, чтобы пользователь успевал прочитать сообщения в Pending UI. Отказался от использования искусственной задержки для сохранения стабильности сети. Обнаружел когда агент 
предлагал варианты реализации промпта.

Стоит отметить что ошибок AI сделал немного. Этому способствовал план, ведущаяся одновременно с работой документация, к которой агент обращался перед каждой новой задачей, большое количество автоматизированных тестов. 
Такой подход расходовал токены, но дал стабильный результат.


# 4. Что решал сам.

- Общая архитектура проекта.
- Отказался от Mirror как основной реализации и оставил реальный сетевой стек как optional scope, потому что требования задания лучше проверяются через имитируемый мультиплеер.
- Верстал UI из сгенерированного атласа.
- Проверял предложения AI до реализации и корректировал их, когда они нарушали архитектуру или преждевременно заходили в следующие этапы.
- Вручную проверял результат в Unity.
- Правил логику генерации игровых полей, чтобы они не съезжали от статичного UI и были похоже на единое игровое поле, а не набор квадратных спрайтов.
- Сам решил попросить AI помочь мне написать этот раздел) А потом удалил получившиеся еще двадцать строк не особо нагруженных смыслом.

Вцелом я ни на одном этапе не оставлял AI трудится самостоятельно и контролировал каждый этап реализации PLAN.md.
