using System;
using DmgEmu.Core;

public interface IDisplay
{
    void SetFrameBuffer(IFrameBuffer fb);
    void Update(IFrameBuffer fb);
    void RunLoop(Action<double> onTick);
}
