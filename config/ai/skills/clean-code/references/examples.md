# Before and after

Language-neutral pseudocode. The codes in brackets point to [smells.md](smells.md) for lookup; you don't need to cite them.

## Intent-revealing names (N1, G16, G25)

Before:

```text
function getThem():
    list1 = []
    for x in theList:
        if x[0] == 4:
            list1.add(x)
    return list1
```

After:

```text
function getFlaggedCells():
    flaggedCells = []
    for cell in gameBoard:
        if cell.isFlagged():
            flaggedCells.add(cell)
    return flaggedCells
```

`x[0] == 4` became a named query on a `Cell` type, and every name now says what it holds.

## Flag argument (F3, G15)

Before:

```text
function render(page, isSuite):
    if isSuite:
        includeSuiteSetup(page)
    includeSetup(page)
    ...
```

After:

```text
function renderForSuite(page):
    includeSuiteSetup(page)
    renderForSingleTest(page)

function renderForSingleTest(page):
    includeSetup(page)
    ...
```

## One level of abstraction and the stepdown rule (G30, G34)

Before:

```text
function registerUser(request):
    if request.email == "" or not request.email.contains("@"):
        throw InvalidInput("email")
    hash = sha256(request.password + SALT)
    db.execute("INSERT INTO users ...", request.email, hash)
    smtp.send(request.email, "Welcome", template.replace("{name}", request.name))
```

After:

```text
function registerUser(request):
    validate(request)
    user = createUser(request)
    save(user)
    sendWelcomeMessage(user)

function validate(request): ...
function createUser(request): ...
function save(user): ...
function sendWelcomeMessage(user): ...
```

The top function reads as a list of steps at one level. Each helper follows it, one level lower.

## Hidden side effect and command-query separation (N7, F2)

Before:

```text
function checkPassword(userName, password):
    user = users.find(userName)
    if user != null and user.passwordMatches(password):
        session.initialize()        // hidden side effect
        return true
    return false
```

After:

```text
function isPasswordValid(userName, password):
    user = users.find(userName)
    return user.exists() and user.passwordMatches(password)

// caller decides about the command
if isPasswordValid(userName, password):
    session.initialize()
```

## Too many arguments (F1)

Before:

```text
function makeCircle(x, y, radius, color, borderWidth)
```

After:

```text
function makeCircle(center: Point, radius, style: Style)
```

## Encapsulated and positive conditionals (G28, G29, G19)

Before:

```text
if not (timer.hasExpired() and not timer.isRecurrent()):
    keep(timer)
```

After:

```text
if not shouldBeDeleted(timer):
    keep(timer)

function shouldBeDeleted(timer):
    return timer.hasExpired() and not timer.isRecurrent()
```

## Switch on type, once (G23)

Before, repeated in `calculatePay`, `isPayday`, `deliverPay`...:

```text
switch employee.type:
    case COMMISSIONED: return calculateCommissionedPay(employee)
    case HOURLY:       return calculateHourlyPay(employee)
    case SALARIED:     return calculateSalariedPay(employee)
```

After, one selection in a factory and polymorphic behavior everywhere else:

```text
function makeEmployee(record): Employee
    switch record.type:
        case COMMISSIONED: return CommissionedEmployee(record)
        case HOURLY:       return HourlyEmployee(record)
        case SALARIED:     return SalariedEmployee(record)

employee.calculatePay()
employee.isPayday(date)
```

In a language with sum types and exhaustive matching, a closed set of variants matched in one module is the
equivalent; the point is that adding a variant changes one place and the compiler finds the rest.

## Train wreck (G36)

Before:

```text
outputDir = context.getOptions().getScratchDir().getAbsolutePath()
stream = createFile(outputDir + "/" + className + ".tmp")
```

After:

```text
stream = context.createScratchFileStream(className)
```

Ask the object to do the job instead of reaching through its internals.

## Errors instead of sentinels (G26)

Before:

```text
function findAccount(id):
    row = db.query(id)
    if row == null: return -1
    return row

result = findAccount(id)
if result == -1: ...      // easy to forget
```

After:

```text
function findAccount(id): Account or AccountNotFound
    ...
```

Absence or failure is in the signature, as an exception or a typed result per the language skill, so the caller can't
ignore it by accident.

## Special case instead of null (error handling)

Before:

```text
employees = getEmployees()
if employees != null:
    for e in employees:
        totalPay += e.getPay()
```

After:

```text
for e in getEmployees():      // returns an empty list, never null
    totalPay += e.getPay()
```

## Hidden temporal coupling (G31)

Before:

```text
gaze()
reticulateSplines()
diveForMoog()
```

After:

```text
gradient = gaze(mood)
splines = reticulateSplines(gradient)
diveForMoog(splines)
```

Each step needs the previous result, so the order can't be shuffled by mistake.

## Wrapped boundary

Before, a vendor type leaks through the whole codebase:

```text
sensors: Map<string, any> = new Map()
sensor = sensors.get(id) as Sensor
```

After:

```text
class Sensors:
    private map: Map<string, any>
    function getById(id): Sensor
        return map.get(id) as Sensor
```

Only `Sensors` knows about the generic map; the rest of the code depends on the interface it needs.

## Explanatory comment replaced by code (C3, G19)

Before:

```text
// check if employee is eligible for full benefits
if (employee.flags & HOURLY_FLAG) and employee.age > 65:
```

After:

```text
if employee.isEligibleForFullBenefits():
```

## Over-commented generated code (C3, C1, N1)

Before:

```text
/// <summary>
/// Processes the order.
/// </summary>
/// <param name="order">The order.</param>
/// <returns>True if successful.</returns>
function process(order): bool
    // Step 1: validate the order
    if order.items.count == 0: return false
    // Step 2: calculate the total
    t = sum(order.items.price)   // total
    // Updated to apply the discount
    if t > 100: t = t * 0.9
    // Step 3: save
    repository.save(order, t)
    return true
```

After:

```text
function placeOrder(order): PlacedOrder or EmptyOrder
    if order.isEmpty(): return EmptyOrder
    total = applyBulkDiscount(order.subtotal())
    return repository.save(order, total)

function applyBulkDiscount(subtotal):
    return subtotal > BULK_DISCOUNT_THRESHOLD ? subtotal * BULK_DISCOUNT_RATE : subtotal
```

Every comment became a name, a constant or a type. Why the bulk discount exists goes to `docs/pricing.md`, and
"Updated to apply the discount" goes to the commit message.