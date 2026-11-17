package com.unity3d.player;

import android.app.Activity;
import android.os.Bundle;
import com.melonloader.port073.Loader073;
import lanchon.dexpatcher.annotation.DexEdit;
import lanchon.dexpatcher.annotation.DexPrepend;

/** Candidate hook for the standard UnityPlayerActivity; not validated on a device. */
@DexEdit
public class UnityPlayerActivity extends Activity {
    @DexPrepend
    @Override protected void onCreate(Bundle state) {
        Loader073.initialize(this);
    }
}
