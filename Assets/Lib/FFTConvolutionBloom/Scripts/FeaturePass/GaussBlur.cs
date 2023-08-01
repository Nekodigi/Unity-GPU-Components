using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GaussBlur
{
    private const string CommandBufferName = nameof(GaussBlurBorderRenderPass);

    private RenderTargetIdentifier _colorTarget;
    private FFTBlur _fftBlur = null;

    private Material mat;

    //PropertyToID�֘A
    private int _fftTempID1, _fftTempID2;

    public GaussBlur(Shader shader)
    {
        mat = CoreUtils.CreateEngineMaterial(shader);
        _fftTempID1 = Shader.PropertyToID("_fftTempID1");//FFT�̓���
        _fftTempID2 = Shader.PropertyToID("_fftTempID2");//FFT�̏o��
    }

    public void Execute()
    {
        if (_fftBlur == null) return;

        var commandBuffer = CommandBufferPool.Get(CommandBufferName);

        commandBuffer.GetTemporaryRT(_fftTempID1, _fftBlur.Descriptor, FilterMode.Bilinear);//���́Bxy�T�C�Y��_fFTBloom.Descriptor����Ȃ��Ă��ǂ�
        commandBuffer.GetTemporaryRT(_fftTempID2, _fftBlur.Descriptor, FilterMode.Bilinear);//�o��


        // ���݂̃J�����`��摜��RenderTexture�ɃR�s�[
        // borderRatio��0.0���傫�����邱�Ƃŉ�ʒ[�ɗ]�����������邱�Ƃ��ł���Bfft�v�Z�Œ[����[�ɉ�荞��Ńu���[�������邽�߂̑Ώ�
        // convolution kernel�̓��e�ɂ��킹�Ē�����
        // _colorTarget��filter moder=clamp�ɂ��邱�Ƃŉ�ʒ[�������L�΂����Ƃ��ł���
        commandBuffer.SetGlobalFloat("_ScalingRatio", 1f / (1f - 2f * _fftBlur.borderRatio));
        commandBuffer.Blit(_fftBlur.target, _fftTempID1, mat);

        //������FFTConvolution���s
        _fftBlur.FFTConvolutionFromRenderPass(commandBuffer, _fftTempID1, _fftTempID2);

        // RenderTexture�����݂�RenderTarget�i�J�����j�ɃR�s�[
        // �]���̕����l��
        commandBuffer.SetGlobalFloat("_ScalingRatio", 1f - 2f * _fftBlur.borderRatio);

        
        commandBuffer.Blit(_fftTempID2, _fftBlur.target, mat);

        

        Graphics.ExecuteCommandBuffer(commandBuffer);

        CommandBufferPool.Release(commandBuffer);
    }

    public void SetFFT(FFTBlur fftBlur) 
    {
        _fftBlur = fftBlur;
    }
}