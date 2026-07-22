namespace HardwareScope.Services;

public static class TemperatureDescriptionService
{
    public static string Describe(string category, string sensorName, bool isThreshold)
    {
        if (isThreshold && sensorName.Contains("警告", StringComparison.OrdinalIgnoreCase))
            return "设备厂商设定的过热警告门槛，不是当前实时温度；接近该数值时应改善散热。";
        if (isThreshold && sensorName.Contains("临界", StringComparison.OrdinalIgnoreCase))
            return "设备厂商设定的临界保护门槛，不是当前实时温度；达到后设备可能降速或保护关机。";
        if (sensorName.Contains("封装", StringComparison.OrdinalIgnoreCase))
            return "处理器芯片封装的综合温度，是判断 CPU 散热和是否可能降频的主要参考。";
        if (sensorName.Contains("核心平均", StringComparison.OrdinalIgnoreCase))
            return "所有处理器核心温度的平均值，用于观察 CPU 整体发热水平。";
        if (sensorName.Contains("核心最高", StringComparison.OrdinalIgnoreCase))
            return "当前最热处理器核心的温度，适合检查局部核心是否接近温度上限。";
        if (category == "处理器" && sensorName.Contains("核心", StringComparison.OrdinalIgnoreCase))
            return "处理器内部核心的温度；高负载时通常上升较快，不同核心之间存在小幅差异属正常。";
        if (sensorName.Contains("热点", StringComparison.OrdinalIgnoreCase))
            return "显卡芯片内部最热测温点，通常高于显卡核心温度，用于判断局部过热情况。";
        if (sensorName.Contains("显存结温", StringComparison.OrdinalIgnoreCase))
            return "显存芯片内部最热结点的温度，高分辨率游戏或显存负载较高时会明显上升。";
        if (sensorName.Contains("显存", StringComparison.OrdinalIgnoreCase))
            return "显卡显存芯片的温度，用于观察显存散热状态。";
        if (category == "显卡" && sensorName.Contains("核心", StringComparison.OrdinalIgnoreCase))
            return "显卡图形核心的主要温度，是判断 GPU 散热和风扇策略的常用指标。";
        if (category == "存储")
            return "硬盘或固态硬盘内部控制器、闪存附近的实时温度；持续过高可能影响性能和寿命。";
        if (category == "主板" && sensorName.Contains("系统", StringComparison.OrdinalIgnoreCase))
            return "主板上的系统环境温度，反映机箱内部整体散热状况。";
        if (category == "主板" && (sensorName.Contains("供电", StringComparison.OrdinalIgnoreCase) || sensorName.Contains("VRM", StringComparison.OrdinalIgnoreCase)))
            return "主板处理器供电区域的温度，高负载时用于判断供电模块散热是否充足。";
        if (category == "主板")
            return "主板传感器测得的实时温度；具体位置由主板厂商定义，通常位于芯片组或板载区域。";
        if (category == "内存")
            return "内存模组或内存传感器的实时温度，用于观察持续高负载下的散热情况。";
        if (category == "处理器")
            return "处理器相关传感器的实时温度，用于观察 CPU 负载与散热变化。";
        if (category == "显卡")
            return "显卡相关传感器的实时温度，用于观察游戏或图形负载下的散热变化。";
        return "该硬件传感器报告的实时温度；传感器具体位置由设备厂商定义。";
    }
}
