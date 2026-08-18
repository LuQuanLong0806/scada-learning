# MC5 · 两轴直线插补(可选加餐:多轴"绑腿跑")

> **系列导航**:[MC1 骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) → [MC2 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md) → [MC3 WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md) → [MC4 UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md) → **MC5 两轴直线插补(可选)** → [MC6 轨迹可视化(可选)](项目实践_MotionControl_MC6_轨迹可视化.md)
> **定位**:单轴运动会了,多轴**协调**才是运控的灵魂。插补(interpolation)= 多根轴"绑腿跑":同起同停、按比例推进,合成轨迹是一条**空间直线** —— 真卡上对应 G01 直线插补指令。本篇是可选加餐:接口声明 + 卡实现 + 第 14 个测试 + 界面演示按钮,全部"贴进类里",做完对插补的体感比看十篇文档都深。做完 [MC6 轨迹可视化](项目实践_MotionControl_MC6_轨迹可视化.md) 再按一次演示按钮,还能**亲眼看到**那条直线。
> **前置**:MC4(13/13 全绿)。
> **预计开发时长**:跟敲 0.5 天。**先只看「📋 需求单」自己想,卡住再看「🛠️ 参考实现」。**

---

## 🎯 本篇交付物

1. `MoveLinear(int[] axes, double[] targets, double speed)`:任意多轴(这里两轴)直线插补,接口声明 + MockMotionCard 实现;
2. 第 14 个测试:中段抓一次两轴位置比,验证"等比推进 + 同时到位";
3. 界面【两轴插补演示】按钮 + 它的可用性规则(两轴全可用才亮);
4. `dotnet test` 14/14 全绿。

---

## 📋 需求单(运控工程师视角 —— 先自己想怎么做)

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-L01 | 插补指令:一次给多根轴各自的目标位置 | 入参形状校验(轴数=目标数、非空);任一轴不满足运动条件,整条指令拒绝 |
| FR-L02 | 等比推进 | 走的过程中任意时刻,两轴位移比例恒定(X 走 50、Y 走 30 → X:Y 恒 5:3) |
| FR-L03 | 同起同停 | 两轴同时启动、同时到位(误差 0,测试 precision: 3) |
| FR-L04 | 一停俱停 | 插补中途急停/被新指令打断 → 所有参与轴就地同时冻结 |
| FR-L05 | 速度语义明确 | speed = "走得最远的那根轴"的速度(矢量速度近似),文档写清 |
| FR-L06 | 界面演示按钮 | 点击 → X→200、Y→120 插补;任何一轴未使能/有报警,按钮灰 |

**先自己想**:
① X 要走 50mm、Y 要走 30mm,你给的速度是 25mm/s —— 每根轴该按什么速度走,才能**同时到**?(这就是插补的全部秘密,一个除法)
② 仿真循环你已经有"每轴一个令牌"的机制,插补要求"一停俱停" —— 令牌怎么发?
③ 单轴运动的步数公式你写过(`距离÷速度÷节拍`),插补时**两根轴距离不一样**,步数按谁算?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 Task.Run / async-await](kp:taskrun) —— 插补仿真循环还是那套后台任务
- [📖 CancellationToken 协作式取消](kp:cancel-token) —— "一个令牌发给所有轴"= 一停俱停
- [📖 IDevice 设备统一抽象](kp:idevice) —— 往接口里加能力时,上层如何零改动受益

---

## 🛠️ 参考实现(五步增量,全部"贴进类里")

### 第 1 步:接口加声明(IMotionCard.cs)

在 `ClearAlarm` 声明之后、`// ———— 模拟卡专用(真卡没有) ————` 之前插入:

```csharp
    // ———— 可选进阶:两轴直线插补 ————

    /// <summary>
    /// 多轴直线插补:各轴同时启动、等比推进、同时到位,走出一条空间直线。
    /// 例:X 从 0→50,Y 从 0→30,任意时刻 X:Y 恒等于 5:3。
    /// </summary>
    MotionResult MoveLinear(int[] axes, double[] targets, double speed);
```

### 第 2 步:模拟卡实现(MockMotionCard.cs)

在 `ClearAlarm` 方法之后、`// ———— 模拟卡专用 ————` 之前插入整个区域:

```csharp
    // ———— 直线插补(可选篇) ————

    public MotionResult MoveLinear(int[] axes, double[] targets, double speed)
    {
        // 入参形状先验一遍:轴号数组与目标数组必须一一对应且非空
        if (axes is null || targets is null || axes.Length == 0 || axes.Length != targets.Length)
            return MotionResult.ParamError;

        lock (_gate)
        {
            if (!_connected) return MotionResult.NotConnected;
            if (speed <= 0) return MotionResult.ParamError;

            // 逐轴检查:轴号 / 使能 / 报警 / 软限位,任何一轴不满足,整条插补指令拒绝
            // (插补是"绑腿跑",一个不能跑整队都不动 —— 真卡同理)
            foreach (var (axis, target) in axes.Zip(targets))
            {
                if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
                if (!_enabled[axis]) return MotionResult.AxisDisabled;
                if (_alarms[axis] is not null) return MotionResult.AlarmActive;
                if (Math.Abs(target) > _softLimit) return MotionResult.ParamError;
            }

            foreach (var axis in axes) _cts[axis]?.Cancel();   // 插补优先:打断各轴在途运动

            // 一个令牌发给所有参与轴 —— 急停/新指令取消它,所有轴同时停(插补的命门:必须同起同停)
            var cts = new CancellationTokenSource();
            foreach (var axis in axes) { _cts[axis] = cts; _moving[axis] = true; }

            var froms = axes.Select(a => _positions[a]).ToArray();
            // 总步数按"走得最远的那根轴"算 —— 步数定了,每根轴再按各自距离等比分步,速度语义 = 最长轴的速度
            var maxDist = 0.0;
            for (var k = 0; k < axes.Length; k++)
                maxDist = Math.Max(maxDist, Math.Abs(targets[k] - froms[k]));

            Task.Run(async () =>
            {
                try
                {
                    var steps = Math.Max(1, (int)Math.Ceiling(maxDist / speed * 1000.0 / _tickMs));
                    for (var i = 1; i <= steps; i++)
                    {
                        await Task.Delay(_tickMs, cts.Token);
                        for (var k = 0; k < axes.Length; k++)
                        {
                            // 等比推进:第 i 步位置 = 起点 + 全程位移 × i/steps
                            // → 任意时刻各轴位移比例恒定,轨迹是空间直线,且同时到位
                            var p = froms[k] + (targets[k] - froms[k]) * i / steps;
                            lock (_gate) _positions[axes[k]] = p;
                            PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], p));
                        }
                    }
                    if (!cts.Token.IsCancellationRequested)
                    {
                        // 一步不多一步不少地精确落点(消除浮点累积误差)
                        for (var k = 0; k < axes.Length; k++)
                        {
                            lock (_gate) _positions[axes[k]] = targets[k];
                            PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], targets[k]));
                        }
                    }
                }
                catch (OperationCanceledException) { /* 急停/打断:各轴就地冻结 */ }
                finally
                {
                    lock (_gate)
                        for (var k = 0; k < axes.Length; k++)
                        {
                            _moving[axes[k]] = false;
                            if (ReferenceEquals(_cts[axes[k]], cts)) _cts[axes[k]] = null;
                        }
                }
            });
            return MotionResult.Ok;
        }
    }
```

💡 **这段实现的四个门道**:

- **等比推进公式是插补的全部数学**:`p = from + (target - from) × i / steps` —— 步数 steps 全体参与轴共用(按最远轴算),每根轴按自己的全程距离等分。两轴的位移比例任何时刻都等于总位移比例 → 轨迹必然是直线,且第 steps 步**同时**到终点。真卡的 G01 在硬件层做的是同一件事(脉冲按比例分配);
- **一个令牌发所有轴** = 一停俱停:单轴运动"每轴一个 CTS"防的是误伤;插补**故意**让所有轴共享同一个 CTS 实例 —— 取消它,所有参与轴的 `Task.Delay` 同时抛取消。两处设计放一起看:"隔离"和"捆绑"都是对的,看场景要哪种;
- **`axes.Zip(targets)` 逐轴检查**:绑腿跑的纪律 —— 一根轴没使能/有报警/超软限位,整条指令拒绝。半阕队伍绑腿跑 = 现场事故;
- **`ReferenceEquals` 清槽**和单轴版同一个理由:被新指令打断时,槽里可能已经是别人(单轴指令或另一条插补)的令牌,只清"还是自己的"那个。

### 第 3 步:第 14 个测试(MockMotionCardTests.cs)

先把类头注释里的清单补上"插补"两个字(改成 `…软限位、回零、插补,全部有据可查。`),然后在「报警链路」与「生命周期」两节之间插入:

```csharp
    // ———— 两轴直线插补(可选篇,MC5 再加) ————

    [Fact]
    public async Task 直线插补_两轴等比推进且同时到位()
    {
        var card = ReadyCard();
        // X: 0→50, Y: 0→30,速度 25 → 全程 2s。任意时刻 X:Y 应恒为 5:3
        Assert.Equal(MotionResult.Ok, card.MoveLinear(new[] { 0, 1 }, new[] { 50.0, 30.0 }, 25));

        await Task.Delay(300);   // 走到中段抓一次比例
        var midX = card.GetAxisPosition(0);
        var midY = card.GetAxisPosition(1);
        Assert.True(midX > 1 && midY > 1, "两轴都应已在运动中");
        Assert.InRange(midX / midY, 5.0 / 3 - 0.1, 5.0 / 3 + 0.1);   // 比例恒定 = 直线

        await WaitUntil(() => !card.IsMoving(0) && !card.IsMoving(1));
        Assert.Equal(50.0, card.GetAxisPosition(0), precision: 3);
        Assert.Equal(30.0, card.GetAxisPosition(1), precision: 3);
    }
```

💡 **`Assert.InRange(midX / midY, 5.0/3 - 0.1, 5.0/3 + 0.1)` 是本篇的灵魂断言**:中段抓拍一次两轴位置,比值落在 5:3 附近 —— 一个数证明了"等比推进"。允许 ±0.1 的容差,因为抓拍瞬间两轴事件上报顺序可能有半个节拍的错位(这是**采样视角**的误差,不是轨迹误差)。

### 第 4 步:界面逻辑(MainForm.cs 三处)

**4a. 构造函数里**,`for` 循环订阅完之后、「卡 → 界面」事件订阅之前加:

```csharp
        // 两轴插补演示按钮(驱动两根轴,不属于任何单轴数组,单独订阅)
        btnLinear.Click += (s, e) => MoveLinearDemo();
```

**4b. `Home()` 方法之后**加演示方法:

```csharp
    /// <summary>
    /// 两轴直线插补演示:X→200、Y→120 同起同停、等比推进。
    /// 按下后盯着两个位置框看 —— 任意时刻 X:Y 恒等于 200:120(≈5:3),这就是"插补走直线"的直观含义。
    /// 速度取轴 1 的速度框,语义 = 走得最远的轴(这里是 X)的速度。
    /// </summary>
    private void MoveLinearDemo()
    {
        var speed = SpeedOf(_txtSpeed[0]);
        var r = _card.MoveLinear(new[] { 0, 1 }, new[] { 200.0, 120.0 }, speed);
        if (r != MotionResult.Ok) { Fail(r, "两轴插补"); return; }
        AppendLog($"两轴插补 → X 200 · Y 120 @ {speed:F0} mm/s(同起同停,等比推进)");
    }
```

**4c. `RefreshUiState()` 末尾**(for 循环的 `}` 之后、方法 `}` 之前)加:

```csharp
        // 插补按钮要求两轴同时可用 —— 插补是"绑腿跑",任何一轴不满足,整条指令都会被卡拒绝
        btnLinear.Enabled = connected
                            && _card.IsAxisEnabled(0) && _card.IsAxisEnabled(1)
                            && string.IsNullOrEmpty(_card.GetAlarmMessage(0))
                            && string.IsNullOrEmpty(_card.GetAlarmMessage(1));
```

### 第 5 步:界面按钮(MainForm.Designer.cs 四处)

**5a.** `btnEstop = new Button();` 之后加一行:

```csharp
        btnLinear = new Button();
```

**5b.** `gbAxis1.Controls.Add(lblSoftLimit1);` 之后加一行:

```csharp
        gbAxis1.Controls.Add(btnLinear);
```

**5c.** `lblSoftLimit1` 配置块之后、`// gbAxis2 ——` 注释之前插入:

```csharp
        //
        // btnLinear —— 两轴直线插补演示:X/Y 同起同停、等比推进(放在轴 1 框里,但驱动两根轴)
        //
        btnLinear.Location = new Point(20, 548);
        btnLinear.Name = "btnLinear";
        btnLinear.Size = new Size(355, 44);
        btnLinear.TabIndex = 15;
        btnLinear.Text = "⇗ 两轴插补演示 → X 200 · Y 120";
        btnLinear.UseVisualStyleBackColor = true;
```

**5d.** 字段区 `private Button btnEstop;` 之后加:

```csharp
    private Button btnLinear;
```

---

## ✅ 验证(沙盒实测输出 + 手动验收)

```bash
dotnet build
```
```
已成功生成。
    0 个警告
    0 个错误
```
```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    14，已跳过:     0，总计:    14，持续时间: 9 s - MotionControl.Tests.dll (net8.0)
```

### 手动验收

| # | 操作 | 预期 |
|---|---|---|
| 1 | 连接 + 使能两轴 | 轴 1 框里出现【⇗ 两轴插补演示】按钮,可点 |
| 2 | 点击它,盯着两个位置框 | X、Y **同时**开始动;任意瞬间 X 读数 ≈ Y 读数 × 5/3(200:120);**同时**停在 200.000 / 120.000 |
| 3 | 只使能轴 1(轴 2 失能) | 插补按钮变灰(绑腿跑,一轴不行整队不动) |
| 4 | 插补运动中点急停 | 两轴位置同时冻结(一停俱停) |
| 5 | 速度框输 20 再点 | 全程明显变慢(速度语义 = 最长轴 X 的速度) |

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] FR-L01 入参形状 + 逐轴检查,一轴不满足整条拒绝
- [ ] FR-L02 等比推进公式落位,测试中段比例断言通过
- [ ] FR-L03 同时到位,双轴 precision: 3 断言通过
- [ ] FR-L04 共享令牌,急停一停俱停(手动验收 4)
- [ ] FR-L05 速度语义 = 最远轴,注释和文档写清
- [ ] FR-L06 按钮可用性两轴全查,14/14 全绿

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"插补我用'共用步数 + 等比分步'实现:步数按走得最远的轴算,每根轴按自己的全程距离等分推进,轨迹必然是直线且同时到位 —— 和真卡 G01 的脉冲分配是同一个数学。"

**追问弹药库**:
- **"怎么保证轨迹是直线?"** —— 所有参与轴共用同一个步数计数 i,第 i 步位置 = 起点 + 全程位移 × i/steps。两轴位移比值恒等于总位移比值,初中学的两点式直线方程,这就是插补的全部数学;
- **"怎么保证同时停?"** —— 所有参与轴共享同一个 CancellationToken,急停/打断取消一次,全部仿真循环在同一节拍抛出取消;
- **"速度是哪根轴的?"** —— 走得最远的那根(最长轴)。真卡矢量速度是 √(vx²+vy²),我取的是近似 —— 演示项目把语义定义清楚比精确建模更重要;
- **"和单轴运动的'每轴一个令牌'矛盾吗?"** —— 不矛盾,是同一机制的两面:单轴要**隔离**(防误伤),插补要**捆绑**(同起同停)。能讲清"什么时候隔离、什么时候捆绑",说明真理解了取消模型。

下一篇:[MC6 · 轨迹可视化 —— 把两轴运动画成 X-Y 轨迹图](项目实践_MotionControl_MC6_轨迹可视化.md)
