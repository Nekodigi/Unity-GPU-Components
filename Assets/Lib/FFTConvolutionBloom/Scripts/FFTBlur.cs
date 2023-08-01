using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class FFTBlur : MonoBehaviour
{
    //Bloom�ݒ����
    const int fftSize = 512;
    private RenderTextureFormat bloomTexFormat = RenderTextureFormat.ARGBFloat;//or ARGBHalf

    //compute shader����
    [SerializeField] private ComputeShader cs;
    [SerializeField] private Texture2D convolutionKernel;//�[�x8bit��png�B1�F������256�����Ȃ�
    RenderTexture convertedKernel;
    public RenderTexture target;
    public Shader scalingShader;
    Material scalingMat;

    private float intensity_back;//convolutionKernel�Ɏw�肵���摜�����邭�Ă��Â��Ă�1�ɂȂ�悤���K������Ӗ�������
    private int kernelFFTX, kernelIFFTX;
    private int kernelFFTY_HADAMARD_IFFTY;
    private int kernelFFTX_DWT, kernelIFFTX_DWT;
    private int kernelFFTY_HADAMARD_IFFTY_DWT;
    private int kernelFFTWY, kernelFFTWY_DWT;
    private int kernelCopySlide;

    private RenderTexture rtWeight = null;//convolutionKernel��FFT�v�Z��̏d�݂�����
    private RenderTexture rtWeightDWT = null;//convolutionKernel��FFT�v�Z��̏d�݂�����BDWT�p

    //PropertyToID�֘A
    private int rt34_i, rt64_i;
    private RenderTargetIdentifier rtWeight_i, rtWeightDWT_i;

    //Descriptor�֘A
    private RenderTextureDescriptor descriptor34, descriptor44, descriptor43, descriptor412, descriptor64, out_descriptor;//RGBA=4,RGB=3
    public RenderTextureDescriptor Descriptor => out_descriptor;//�o�͂̉摜�̑傫��


    GaussBlur gaussBlur;
    [SerializeField] [Range(0f, 0.499f)] public float borderRatio;
    [SerializeField] [Range(0f, 10f)] public float blurScale = 1;

    private void Awake()
    {
        //RenderFeature���炱���̊֐����Ăяo����悤��
        gaussBlur = new GaussBlur(scalingShader);
        gaussBlur.SetFFT(this.GetComponent<FFTBlur>());
        scalingMat = CoreUtils.CreateEngineMaterial(scalingShader);

        convolutionKernel.wrapMode = TextureWrapMode.Clamp;
        convertedKernel = new RenderTexture(fftSize, fftSize, 0, RenderTextureFormat.ARGBFloat);
        convertedKernel.enableRandomWrite = true;
        convertedKernel.Create();

        //descriptor�Z�b�g
        DescriptorInit();
        ShaderIDInit();

        //kernel�Z�b�g
        SetKernles();

        //RenderTexture�֘A
        rtWeight = new RenderTexture(descriptor43);
        rtWeight.Create();
        rtWeight_i = new RenderTargetIdentifier(rtWeight);
        rtWeightDWT = new RenderTexture(descriptor412);
        rtWeightDWT.Create();
        rtWeightDWT_i = new RenderTargetIdentifier(rtWeightDWT);

        //convolutionKernel����fft���Weight���v�Z
        ResizeKernel(1, 1f);
    }

    public void ResizeKernel(float scale, float aspect)
    {
        scalingMat.SetFloat("_ScalingRatioX", 1 / (scale + 0.05f) * aspect);
        scalingMat.SetFloat("_ScalingRatioY", 1 / (scale + 0.05f));
        scalingMat.SetFloat("_ScalingRatio", 0);
        Graphics.Blit(convolutionKernel, convertedKernel, scalingMat);

        CreateWeight();
    }

    public RenderTexture Execute(RenderTexture target)
    {
        this.target = target;
        ResizeKernel(blurScale, 1.77777f);

        gaussBlur.Execute();
        return target;
    }

    void SetKernles()
    {
        kernelFFTX = cs.FindKernel("FFTX");
        kernelIFFTX = cs.FindKernel("IFFTX");
        kernelFFTY_HADAMARD_IFFTY = cs.FindKernel("FFTY_HADAMARD_IFFTY");
        kernelFFTWY = cs.FindKernel("FFTWY");
        kernelFFTX_DWT = cs.FindKernel("FFTX_DWT");
        kernelIFFTX_DWT = cs.FindKernel("IFFTX_DWT");
        kernelFFTY_HADAMARD_IFFTY_DWT = cs.FindKernel("FFTY_HADAMARD_IFFTY_DWT");
        kernelFFTWY_DWT = cs.FindKernel("FFTWY_DWT");
        kernelCopySlide = cs.FindKernel("CopySlide");
    }

    void DescriptorInit()
    {
        descriptor34 = new RenderTextureDescriptor(fftSize / 4 * 3 + 2, fftSize, bloomTexFormat, 0);
        descriptor34.enableRandomWrite = true;
        descriptor64 = new RenderTextureDescriptor(fftSize / 4 * 6, fftSize, bloomTexFormat, 0);
        descriptor64.enableRandomWrite = true;
        descriptor43 = new RenderTextureDescriptor(fftSize, fftSize / 4 * 3 + 2, bloomTexFormat, 0);
        descriptor43.enableRandomWrite = true;
        descriptor412 = new RenderTextureDescriptor(fftSize, fftSize / 4 * 12, bloomTexFormat, 0);
        descriptor412.enableRandomWrite = true;
        descriptor44 = new RenderTextureDescriptor(fftSize, fftSize, bloomTexFormat, 0);
        descriptor44.enableRandomWrite = true;
        out_descriptor = new RenderTextureDescriptor(fftSize, fftSize, bloomTexFormat, 0);
        out_descriptor.enableRandomWrite = true;
    }

    void ShaderIDInit()
    {
        rt34_i = Shader.PropertyToID("_rt34");
    }

    /// Convolution kernel�̎��OFFT�v�Z
    /// ���ł�intensity_back�̌v�Z��
    private void CreateWeight()
    {
        RenderTexture rtex1 = new RenderTexture(descriptor44);
        rtex1.Create();
        RenderTexture rtex2 = new RenderTexture(descriptor44);
        rtex2.Create();
        RenderTexture rtex3 = new RenderTexture(descriptor34);
        rtex3.Create();
        RenderTexture rtex4 = new RenderTexture(descriptor64);
        rtex4.Create();

        uint[] res = new uint[4];
        for (int i = 0; i < 4; i++) res[i] = 0;
        ComputeBuffer SumBuf = new ComputeBuffer(4, 4);//R,G,B���ꂼ��ɂ�����S��ʂ̒l�̑��v�Bintensity_back�̌v�Z�̂���
        SumBuf.SetData(res);
        Graphics.Blit(convertedKernel, rtex2);//ComputeShader��RenderTexture���������Ȃ��̂�

        //�e�N�X�`���̒��S��0,0�Ɉړ��B���ł�SumBuf�ɑS��f�l���v����
        cs.SetBuffer(kernelCopySlide, "SumBuf", SumBuf);
        cs.SetTexture(kernelCopySlide, "Tex_ro", rtex2);
        cs.SetTexture(kernelCopySlide, "Tex", rtex1);
        cs.SetBool("use256x4", false);
        cs.SetInt("width", fftSize);
        cs.SetInt("height", fftSize);
        cs.SetBool("isWeightcalc", false);

        cs.Dispatch(kernelCopySlide, fftSize / 8, fftSize / 8, 1);//����Œ��S��0,0�Ɉړ��������̂�rtex1�ɂ͂���

        //�������ݍ��݂̂ق�
        //1
        cs.SetTexture(kernelFFTX, "Tex_ro", rtex1);
        cs.SetTexture(kernelFFTX, "Tex", rtex3);
        cs.Dispatch(kernelFFTX, fftSize, 1, 1);
        //2
        cs.SetTexture(kernelFFTWY, "Tex_ro", rtex3);
        cs.SetTexture(kernelFFTWY, "Tex", rtWeight);
        cs.Dispatch(kernelFFTWY, fftSize / 4 * 3 + 2, 1, 1);//FFTY_HADAMARD_IFFTY�ł�Texture�A�N�Z�X�������̂��ߓ]�u���Ă���


        //�������+�������ݍ��݂̂ق�(x�Ő���,y�Ő����Ȃ̂�4�{)
        cs.SetBool("isWeightcalc", true);
        //1
        cs.SetTexture(kernelFFTX_DWT, "Tex_ro", rtex1);
        cs.SetTexture(kernelFFTX_DWT, "Tex", rtex4);
        cs.Dispatch(kernelFFTX_DWT, fftSize, 1, 1);
        //2
        cs.SetTexture(kernelFFTWY_DWT, "Tex_ro", rtex4);
        cs.SetTexture(kernelFFTWY_DWT, "Tex", rtWeightDWT);
        cs.Dispatch(kernelFFTWY_DWT, fftSize / 4 * 6, 1, 1);//FFTY_HADAMARD_IFFTY�ł�Texture�A�N�Z�X�������̂��ߓ]�u���Ă���
        cs.SetBool("isWeightcalc", false);

        //intensity_back�̌v�Z
        SumBuf.GetData(res);
        intensity_back = 1.0f * res[0];
        for (int i = 1; i < 3; i++)
            intensity_back = Mathf.Max(intensity_back, res[i]);
        if (intensity_back != 0.0f)
        {
            intensity_back = 255.0f / intensity_back;
        }
        else
        {
            Debug.Log("ConvolutionKernel���^�����ł�");
        }

        //Release
        RenderTexture.active = null;//���ꂪ�Ȃ���rtex2�̉����Releasing render texture that is set to be RenderTexture.active!����������
        rtex4.Release();
        rtex3.Release();
        rtex2.Release();
        rtex1.Release();
        SumBuf.Release();
    }


    /// srcID�摜����͂����FFT Convolution�����s�������ʂ�dstID�̉摜�ɂ͂���
    /// srcID�̉摜�T�C�Y��fft�ł���T�C�Y����Ȃ����src_w,src_h����͂��邱��
    /// �������ݍ��݂ł��邱�Ƃɒ���(�摜�̒[����[�ɉ�荞��ł��܂�)
    public void FFTConvolutionFromRenderPass(CommandBuffer commandBuffer, int srcID, int dstID, int src_w = -1, int src_h = -1)
    {
        if (rtWeight == null) Awake();
        if (src_w == -1) src_w = descriptor44.width;
        if (src_h == -1) src_h = descriptor44.height;

        commandBuffer.GetTemporaryRT(rt34_i, descriptor34);

        commandBuffer.SetComputeFloatParam(cs, "_intensity", 1 * intensity_back);//intensity_back����Z���邱�ƂŎ������邳����
        commandBuffer.SetComputeIntParam(cs, "width", src_w);
        commandBuffer.SetComputeIntParam(cs, "height", src_h);

        //1
        commandBuffer.SetComputeTextureParam(cs, kernelFFTX, "Tex_ro", srcID);
        commandBuffer.SetComputeTextureParam(cs, kernelFFTX, "Tex", rt34_i);
        commandBuffer.DispatchCompute(cs, kernelFFTX, fftSize, 1, 1);

        //2
        commandBuffer.SetComputeTextureParam(cs, kernelFFTY_HADAMARD_IFFTY, "Tex", rt34_i);
        commandBuffer.SetComputeTextureParam(cs, kernelFFTY_HADAMARD_IFFTY, "Tex_ro", rtWeight_i);
        commandBuffer.DispatchCompute(cs, kernelFFTY_HADAMARD_IFFTY, fftSize / 4 * 3 + 2, 1, 1);

        //3
        commandBuffer.SetComputeTextureParam(cs, kernelIFFTX, "Tex_ro", rt34_i);
        commandBuffer.SetComputeTextureParam(cs, kernelIFFTX, "Tex", dstID);
        commandBuffer.DispatchCompute(cs, kernelIFFTX, fftSize, 1, 1);
        return;
    }

    /// srcID�摜����͂����FFT Convolution�����s�������ʂ�dstID�̉摜�ɂ͂���
    /// srcID�̉摜�T�C�Y��fft�ł���T�C�Y����Ȃ����src_w,src_h����͂��邱��
    /// ������+������ŉ�荞��ł��܂��e�����Ȃ�����ver�B���U�׏d�ϊ�DWT���g���Ă��邪3.3�{�d��
    /// ������g���q���ꉞ����
    public void FFTConvolutionFromRenderPassDWT(CommandBuffer commandBuffer, int srcID, int dstID, int src_w = -1, int src_h = -1)
    {
        if (rtWeight == null) Awake();
        if (src_w == -1) src_w = descriptor44.width;
        if (src_h == -1) src_h = descriptor44.height;

        commandBuffer.GetTemporaryRT(rt64_i, descriptor64);

        commandBuffer.SetComputeFloatParam(cs, "_intensity", 1 * intensity_back);//intensity_back����Z���邱�ƂŎ������邳����
        commandBuffer.SetComputeIntParam(cs, "width", src_w);
        commandBuffer.SetComputeIntParam(cs, "height", src_h);

        //1
        commandBuffer.SetComputeTextureParam(cs, kernelFFTX_DWT, "Tex_ro", srcID);
        commandBuffer.SetComputeTextureParam(cs, kernelFFTX_DWT, "Tex", rt64_i);
        commandBuffer.DispatchCompute(cs, kernelFFTX_DWT, fftSize, 1, 1);

        //2
        commandBuffer.SetComputeTextureParam(cs, kernelFFTY_HADAMARD_IFFTY_DWT, "Tex", rt64_i);
        commandBuffer.SetComputeTextureParam(cs, kernelFFTY_HADAMARD_IFFTY_DWT, "Tex_ro", rtWeightDWT_i);
        commandBuffer.DispatchCompute(cs, kernelFFTY_HADAMARD_IFFTY_DWT, fftSize / 4 * 6, 1, 1);

        //3
        commandBuffer.SetComputeTextureParam(cs, kernelIFFTX_DWT, "Tex_ro", rt64_i);
        commandBuffer.SetComputeTextureParam(cs, kernelIFFTX_DWT, "Tex", dstID);
        commandBuffer.DispatchCompute(cs, kernelIFFTX_DWT, fftSize, 1, 1);

        return;
    }


    private void OnDisable()
    {
        rtWeight.Release();
        rtWeightDWT.Release();
    }
}

