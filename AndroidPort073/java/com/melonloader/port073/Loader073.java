package com.melonloader.port073;

import android.app.Activity;
import android.content.res.AssetManager;
import android.util.Log;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/** Experimental entry point. Not wired to the legacy patcher. */
public final class Loader073 {
    private static final String TAG = "MelonLoader073";
    private Loader073() { }
    private static native boolean initializeNative(String baseDirectory);

    public static boolean initialize(Activity activity) {
        try {
            File base = new File(activity.getFilesDir(), "melonloader073");
            if (!base.isDirectory() && !base.mkdirs())
                throw new IOException("Cannot create " + base);
            AssetManager assets = activity.getAssets();
            copyTree(assets, "MelonLoader", new File(base, "MelonLoader"));
            copyTree(assets, "dotnet", new File(base, "dotnet"));
            // Unity's asset paths are not ordinary filesystem paths on Android.
            // The 0.7.3 reader expects a filesystem Game_Data directory.
            copyTree(assets, "bin/Data", new File(base, "Game_Data"));
            copyGameAssembly(activity, new File(base, "GameAssembly.so"));
            System.loadLibrary("melonloader073_bridge");
            if (!initializeNative(base.getAbsolutePath())) {
                Log.e(TAG, "Bootstrap initialization failed");
                return false;
            }
            return true;
        } catch (Exception | LinkageError e) {
            Log.e(TAG, "Unable to initialize experimental 0.7.3 loader", e);
            return false;
        }
    }

    private static void copyTree(AssetManager assets, String source, File target) throws IOException {
        String[] entries = assets.list(source);
        if (entries == null) throw new IOException("Cannot list asset: " + source);
        if (entries.length == 0) {
            File parent = target.getParentFile();
            if (!parent.isDirectory() && !parent.mkdirs()) throw new IOException("Cannot create " + parent);
            try (InputStream in = assets.open(source)) { copy(in, target); }
            return;
        }
        if (!target.isDirectory() && !target.mkdirs()) throw new IOException("Cannot create " + target);
        for (String child : entries) {
            if (child.equals(".") || child.equals("..") || child.contains("/") || child.contains("\\"))
                throw new IOException("Invalid asset name");
            copyTree(assets, source + "/" + child, new File(target, child));
        }
    }

    private static void copyGameAssembly(Activity activity, File target) throws IOException {
        try (ZipFile apk = new ZipFile(activity.getApplicationInfo().sourceDir)) {
            ZipEntry entry = apk.getEntry("lib/arm64-v8a/libil2cpp.so");
            if (entry == null) throw new IOException("arm64 libil2cpp.so missing from base APK");
            try (InputStream in = apk.getInputStream(entry)) { copy(in, target); }
        }
    }

    private static void copy(InputStream in, File target) throws IOException {
        // Write to a sibling and atomically replace; interrupted copies must not become inputs.
        File tmp = new File(target.getPath() + ".tmp");
        try {
            try (FileOutputStream out = new FileOutputStream(tmp)) {
                byte[] buffer = new byte[32768];
                int count;
                while ((count = in.read(buffer)) != -1) out.write(buffer, 0, count);
                out.getFD().sync();
            }
            if (!tmp.renameTo(target)) throw new IOException("Cannot replace " + target);
        } finally {
            if (tmp.exists() && !tmp.delete()) Log.w(TAG, "Cannot remove " + tmp);
        }
    }
}
