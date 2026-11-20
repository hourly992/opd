"""Tileable hand-authored albedo and normal maps for the rose granite."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image


parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
out = parser.parse_args().output
out.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(216)
size = 1024


def smooth_noise(res):
    grid = rng.uniform(0, 1, (res, res))
    u = np.linspace(0, res, size, endpoint=False)
    i = np.floor(u).astype(int)
    j = (i+1) % res
    h = u-i
    h = h*h*(3-2*h)
    return ((1-h[:,None])*((1-h[None,:])*grid[i[:,None],i[None,:]]+h[None,:]*grid[i[:,None],j[None,:]]) +
            h[:,None]*((1-h[None,:])*grid[j[:,None],i[None,:]]+h[None,:]*grid[j[:,None],j[None,:]]))


fine = .45*smooth_noise(416)+.36*smooth_noise(248)+.19*smooth_noise(104)
veins = smooth_noise(24)
pattern = fine*.86+veins*.14
colors = np.array([[82,70,69],[132,111,109],[171,129,121],[176,126,117],[181,139,133],[188,167,158],[227,210,196]])
knots = [.11,.34,.414,.49,.57,.65,.81]
rgb = np.stack([np.interp(pattern,knots,colors[:, channel]) for channel in range(3)],axis=-1)
variation = 1+(smooth_noise(5)-.5)*.087
rgb *= variation[:,:,None]
Image.fromarray(np.clip(rgb,0,255).astype("uint8")).save(out/"Granite_Albedo.png", optimize=True)
height = .7*fine+.3*veins
dx = (np.roll(height,1,axis=1)-np.roll(height,-1,axis=1))*.13
dy = (np.roll(height,-1,axis=0)-np.roll(height,1,axis=0))*.13
n = np.stack([dx,dy,np.ones(dx.shape)],axis=-1)
n /= np.linalg.norm(n,axis=-1)[:,:,None]
Image.fromarray(((n*.5+.5)*255).round().astype("uint8")).save(out/"Granite_Normal.png",optimize=True)
