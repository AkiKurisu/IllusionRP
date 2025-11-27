# Screen Space Subsurface Scattering

## Diffusion Profile

The Diffusion Profile in IllusionRP is basically the same as HDRP. For documentation, please refer to [HDRP - Diffusion Profile](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@16.0//manual/Diffusion-Profile.html).

![Diffusion Profile](./images/diffusion_profile.png)

> [!TIP]
> It should be noted that IllusionRP has removed the default profile originally designed by HDRP. Now the first one in the profile list is the actual default profile.


# Global Illumination

IllusionRP use Main Light's `Indirect Multiplyer` to control the intensity of GI.