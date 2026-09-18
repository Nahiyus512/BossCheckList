namespace BossChecklist;

/// <summary>
/// 清单筛选状态。配置文件里存的是 int（0/1/2），因为配置系统只认 int/bool/float/string。
/// 名字和原模组保持一致，比较时转一下：(FilterType)cfg.FilterBosses 。
/// </summary>
internal enum FilterType
{
    Show = 0,
    HideWhenCompleted = 1,
    Hide = 2
}

/// <summary>聊天栏提示的显示方式。配置文件里同样存 int。</summary>
internal enum MessageType
{
    Disabled = 0,
    Generic = 1,
    Unique = 2
}

/// <summary>清单里打勾的样式。配置文件里同样存 int。</summary>
internal enum CheckType
{
    Check_Empty = 0,
    Check_X = 1,
    X_Empty = 2,
    StrikeThrough = 3
}
