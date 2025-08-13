namespace Athena.Invalidation.Engine.Core;

/// <summary>
/// 무효화 실행 컨텍스트 구현
/// </summary>
public class InvalidationContext(InvalidationTrigger trigger) : BaseInvalidationContext(trigger)
{
    public override IInvalidationContext Clone()
    {
        var clone = new InvalidationContext(Trigger);
        CopyPropertiesTo(clone);
        return clone;
    }
}
