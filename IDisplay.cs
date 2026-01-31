using System;
using GB;

public interface IDisplay
{
    void SetFrameBuffer(IFrameBuffer fb);
    void Update(IFrameBuffer fb);
    void RunLoop(Action<double> onTick);
}
