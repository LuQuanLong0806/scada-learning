# ⚙️ 亲手造发动机 · 注释工作纸(MotionControl 卷)

> **玩法**:每行代码只给**中文注释**,不给代码。你开一个空白 .cs 文件,照着注释把代码写出来——**能根据注释写出代码 = 这个文件真正属于你**。
> **覆盖 2 张工作纸**(🧠核心逻辑档):①MockMotionCard 单轴运动核心段(检查链 + StartMotionLocked 运动循环/急停冻结/回零贴目标/软限位保护,~100 行)②MoveLinear 插补核心段(~50 行)。
> **规则**:🔧 = 脚手架已给(照抄);🧠① = 你要写的行(注释说清干什么,自己想语法)。写完对照原文件 diff,再替换跑测试。

---

## 使用方法(每张工作纸都一样,3 步)

1. **新建空白文件**写到标注的 📂 路径(先别覆盖原文件——把原文件改名 `XXX.参考.cs` 备份)
2. **照注释写代码**,卡壳超 15 分钟 → 翻[逐行导读](项目逐行导读_MotionControl_从零到吃透.md)对应站(那里每行都有讲解)
3. **验收三连**:`dotnet build` 0 错误 → `dotnet test` **14/14 全绿** → 和 `.参考.cs` diff,差异逐个问自己"为什么它那样写"
   - 测试不过 = 你写的零件装不回整机 → 定位修复(这本身就是最好的学习)

---

## 工作纸 1 · MockMotionCard 单轴运动核心段(~100 行,模拟卡心脏)

> 📂 `MotionControl/src/MotionControl/Device/MockMotionCard.cs` 的 302-377 行(检查链 + StartMotionLocked)· 对应[导读第③站](项目逐行导读_MotionControl_从零到吃透.md) · 验收:14/14 全绿
> **做法**:原文件其余部分(字段/构造/其他方法)保留不动,只把下面两段挖空重写。

**1a · 运动前检查链 CheckMotionLocked(302-310 行)**

```csharp
    // 🔧 方法签名已给:返回 MotionResult,入参轴号+速度(调用方已持 _gate 锁)
    private MotionResult CheckMotionLocked(int axis, double speed)
    {
        // 🧠① 五连检查,按"越靠前的越廉价"排序,任一不过立刻返回对应错误码:
        //    轴号不合法(调 CheckIndex)→ AxisIndexError
        // 🧠② 未连接 → NotConnected
        // 🧠③ 速度 ≤ 0 → ParamError
        // 🧠④ 该轴未使能(查 _enabled 数组)→ AxisDisabled
        // 🧠⑤ 该轴有报警(_alarms 槽非 null)→ AlarmActive
        // 🧠⑥ 全过 → Ok
    }
```

**1b · 单轴运动仿真 StartMotionLocked(316-377 行,★主菜)**

```csharp
    // 🔧 方法签名已给:启动一段单轴匀速运动(点动/定位/回零最终都落到这;调用方已持锁)
    private void StartMotionLocked(int axis, double target, double speed, bool jog)
    {
        // 🧠① 打断语义:取消该轴旧令牌(可空条件下 Cancel),再 new 新令牌源存进 _cts 槽
        // 🧠② 置 _moving[axis] = true
        // 🧠③ 快照:from = 当前位置;dist = target - from
        // 🧠④ 步数 = Math.Max(1, Ceiling(|dist| / speed × 1000 / _tickMs))——Max(1,…) 防 v1 短距离除零瞬移
        // 🧠⑤ 每步位移 step = dist / steps
        // 🧠⑥ Task.Run 启动后台异步任务(内部逻辑见下)
        Task.Run(async () =>
        {
            try
            {
                // 🧠⑦ for i 从 1 到 steps(含),每步:
                {
                    // 🧠⑧ await Task.Delay 一个节拍,带令牌(睡梦中能被取消叫醒)
                    // 🧠⑨ 第 i 步位置 p = from + step × i(几何公式,无累积误差)
                    // 🧠⑩ 持 _gate 锁写 _positions[axis](注意锁体只包写位置这一行!)
                    // 🧠⑪ 锁外触发 PositionChanged 事件,参数(轴号, p)
                    // 🧠⑫ 点动专属保护(只有 jog 才查):|p| 已顶到软限位(留 1e-9 余量)→
                    //     a. 位置夹到 [-_softLimit, _softLimit](Math.Clamp)
                    //     b. 锁内置报警串:($"触发{正/负}软限位 {限位:F0}mm,已自动停止")+ 再发一次夹紧后的位置事件
                    //     c. 锁外触发 AlarmChanged 事件(isActive: true)
                    //     d. break 退出循环
                }
                // 🧠⑬ 定位/回零走完全程后(令牌没被取消 且 不是 jog):
                //     持锁把位置精确贴到 target,锁外再发一次位置事件(消除浮点尾差,保重复定位精度)
            }
            // 🧠⑭ catch OperationCanceledException:空处理——急停/松手/被打断,位置就地冻结
            finally
            {
                // 🧠⑮ 持锁两件事:_moving[axis] 复位 false;
                //     只有槽里还是"自己这个令牌"(ReferenceEquals)才清空 _cts[axis]——防误删打断者的新令牌
            }
        });
    }
```

**对照要点**(写完 diff 时重点看):⑩⑪ **写位置持锁、事件在锁外**的两行分工;⑫ Math.Clamp + 报警 + break 的三连顺序;⑮ ReferenceEquals 的身份验证——这三处是本卡并发正确性的全部命门。

---

## 工作纸 2 · MoveLinear 插补核心段(~50 行,绑腿跑)

> 📂 `MotionControl/src/MotionControl/Device/MockMotionCard.cs` 的 204-277 行(MoveLinear)· 对应[导读第⑤站](项目逐行导读_MotionControl_从零到吃透.md) · 验收:14/14 全绿(直线插补_两轴等比推进且同时到位 直接考它)

```csharp
    // 🔧 方法签名已给:public MotionResult MoveLinear(int[] axes, double[] targets, double speed)
    {
        // 🧠① 入参形状先验:任一数组为 null / axes 空 / 两数组长度不等 → ParamError
        // 🧠② 入大锁 _gate:
        //     a. 未连接 → NotConnected;速度 ≤ 0 → ParamError
        // 🧠③ 逐轴全检(axes.Zip(targets) 配对遍历):轴号非法 / 未使能 / 有报警 / 目标超软限位,
        //     任何一轴不满足立刻返回对应错误码——绑腿跑,一个不能跑整队都不动
        // 🧠④ 插补优先:取消每根参与轴的在途运动(foreach 可空 Cancel)
        // 🧠⑤ new 一个令牌源 cts,foreach 塞进每根参与轴的 _cts 槽 + 置 _moving
        //    ——注意:所有轴共享同一个令牌实例(急停/新指令一取消,全员同时停)
        // 🧠⑥ 快照每轴起点 froms(LINQ Select + ToArray)
        // 🧠⑦ 求走得最远的轴的距离 maxDist(foreach Math.Max)
        // 🧠⑧ Task.Run 后台任务:
        Task.Run(async () =>
        {
            try
            {
                // 🧠⑨ 总步数 = Math.Max(1, Ceiling(maxDist / speed × 1000 / _tickMs))——按最远轴算,速度语义=最长腿
                // 🧠⑩ 外层循环 i 从 1 到 steps:先 await Task.Delay(节拍, 共享令牌),
                //     再内层 foreach 每根轴 k:
                //       第 i 步位置 = froms[k] + (targets[k] - froms[k]) × i / steps(公共进度等比分步!)
                //       持锁写位置 → 锁外发位置事件(和单轴版同款分工)
                // 🧠⑪ 全程走完且令牌未被取消:foreach 每轴精确贴到 targets[k](持锁写+锁外发事件)
            }
            // 🧠⑫ catch OperationCanceledException:空处理——各轴就地冻结
            finally
            {
                // 🧠⑬ 持锁 foreach 每轴:复位 _moving;
                //     ReferenceEquals 验身份后才清 _cts 槽(和单轴版同款防误删)
            }
        });
        // 🧠⑭ return Ok(指令受理,运动在后台推进)
    }
```

**对照要点**:⑤ 共享一个令牌 vs 单轴版每轴一个令牌的**反差**及理由;⑩ `i / steps` 公共进度是插补的全部数学(改成整数除法试试,插补测试立刻红);⑪ 被取消就不"补刀"贴目标。

---

## 完工仪式

2 张纸全过(14/14 全绿 + diff 能讲出差异原因)后,做两件事:

1. **git commit 你的版本**:`git commit -m "feat: 亲手重写运控发动机 2 件(工作纸验证通过)"`——这是"我写的"最硬证据;
2. **脱稿讲 3 分钟**:不看任何材料,把"一次按下插补按钮,到轨迹图上画出一条直线"讲一遍(指令→合同→共享令牌→等比推进→同拍采样→画线,录音更好)——讲得顺,发动机真的在你手里了。

> 姐妹卷:[DaqMonitor 注释工作纸](亲手造发动机_注释工作纸_DaqMonitor.md)(采集线 5 张,已发)——两卷都过,接口抽象 + 节拍仿真 + 令牌联动这套"发动机"你就造了两遍。
