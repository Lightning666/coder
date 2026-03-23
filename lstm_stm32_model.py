from pathlib import Path

import numpy as np
import tensorflow as tf
from tensorflow.keras import layers, models, regularizers

# =========================
# 1) 面向 STM32 的固定输入配置
# =========================
# 输入是长度为 64 的时间序列，每个时间步 8 个水质特征。
# 这与题目要求的 (time_steps, features) 完全一致。
TIME_STEPS = 64
FEATURES = 8

# 任务类型可二选一："classification" 或 "regression"
# - classification: 输出层使用 softmax
# - regression: 输出层使用线性激活
TASK_TYPE = "classification"

# 分类示例中，假设输出 3 个类别。
# 例如：0=优、1=良、2=差。你可以根据自己的标签数量修改。
NUM_CLASSES = 3

# 回归示例中，假设输出 1 个连续值。
REGRESSION_OUTPUTS = 1

# 训练时批大小可以灵活设置；部署到 STM32 / X-CUBE-AI 时通常固定 batch=1。
BATCH_SIZE = 16
EPOCHS = 3
TRAIN_SAMPLES = 256
VAL_SAMPLES = 64
LEARNING_RATE = 1e-3
MODEL_PATH = Path("lstm_stm32_model.h5")

# 是否启用“伪量化”节点。
# 这是可选项，用于让导出前的张量范围更接近 8-bit 量化场景。
# 如果后续在工具链中遇到兼容性问题，可直接关闭。
USE_FAKE_QUANT = False


class OptionalFakeQuant(layers.Layer):
    """可选伪量化层。

    说明：
    - 仅使用 TensorFlow 原生 fake quant 算子。
    - 该层放在靠近输出的位置，帮助观察量化前后的张量范围。
    - 若不想在模型图中保留该节点，可将 USE_FAKE_QUANT=False。
    """

    def __init__(self, min_value=-1.0, max_value=1.0, enabled=False, **kwargs):
        super().__init__(**kwargs)
        self.min_value = float(min_value)
        self.max_value = float(max_value)
        self.enabled = bool(enabled)

    def call(self, inputs, training=None):
        if not self.enabled:
            return inputs
        return tf.quantization.fake_quant_with_min_max_vars(
            inputs,
            min=self.min_value,
            max=self.max_value,
            num_bits=8,
            narrow_range=False,
        )

    def get_config(self):
        config = super().get_config()
        config.update(
            {
                "min_value": self.min_value,
                "max_value": self.max_value,
                "enabled": self.enabled,
            }
        )
        return config


def build_stm32_friendly_lstm_model(
    task_type="classification",
    time_steps=TIME_STEPS,
    features=FEATURES,
    num_classes=NUM_CLASSES,
    regression_outputs=REGRESSION_OUTPUTS,
    use_fake_quant=USE_FAKE_QUANT,
):
    """构建适合 STM32 部署的轻量级 LSTM 模型。

    设计要点：
    1. 仅使用 1 个 LSTM 层，避免结构过重；如需扩展，也仍可控制在最多 2 层。
    2. LSTM 单元数为 32，小于题目限定的 64，能显著降低参数量与 RAM/Flash 压力。
    3. 仅使用 1 个隐藏 Dense 层（16 单元），低于 32 的上限。
    4. 不使用 Embedding、BatchNormalization、复杂激活等不利于 MCU 部署的算子。
    5. LSTM 保持默认 activation='tanh'、recurrent_activation='sigmoid'。
    6. 不设置 go_backwards=True，也不设置 unroll=True，兼容 X-CUBE-AI 的要求更好。
    7. 加入轻微 L2 正则和可选 Dropout，使权重幅值更稳定，利于后续 8-bit 量化。
    """
    inputs = layers.Input(shape=(time_steps, features), name="water_quality_sequence")

    # 使用单层 LSTM：
    # - units=32，满足“建议使用 32 或 16”的要求。
    # - 默认 activation=tanh, recurrent_activation=sigmoid。
    # - recurrent_dropout 默认为 0，避免引入额外复杂性。
    x = layers.LSTM(
        units=32,
        return_sequences=False,
        kernel_regularizer=regularizers.l2(1e-5),
        recurrent_regularizer=regularizers.l2(1e-5),
        name="lstm_1",
    )(inputs)

    # Dropout 仅在训练时启用；推理/部署时不会生效。
    x = layers.Dropout(rate=0.10, name="dropout_train_only")(x)

    # 轻量全连接层，参数量小，便于 MCU Flash 存储。
    x = layers.Dense(
        units=16,
        activation="relu",
        kernel_regularizer=regularizers.l2(1e-5),
        name="dense_1",
    )(x)

    # 可选伪量化层：帮助控制输出前张量范围。
    # 分类通常取 [-6, 6] 足以覆盖常见 logits；
    # 回归可根据目标值范围调整 min/max。
    x = OptionalFakeQuant(
        min_value=-6.0 if task_type == "classification" else -2.0,
        max_value=6.0 if task_type == "classification" else 2.0,
        enabled=use_fake_quant,
        name="optional_fake_quant",
    )(x)

    if task_type == "classification":
        outputs = layers.Dense(
            units=num_classes,
            activation="softmax",
            name="output",
        )(x)
    elif task_type == "regression":
        outputs = layers.Dense(
            units=regression_outputs,
            activation="linear",
            name="output",
        )(x)
    else:
        raise ValueError("task_type 必须是 'classification' 或 'regression'.")

    model = models.Model(inputs=inputs, outputs=outputs, name="stm32_lstm_model")
    return model


def print_model_size_info(model):
    """打印模型参数量与近似参数存储大小。"""
    total_params = model.count_params()

    # float32 权重每个参数 4 字节。
    total_bytes_fp32 = total_params * 4
    total_kb_fp32 = total_bytes_fp32 / 1024.0

    # 仅作量化后的理论存储参考，便于对 STM32 Flash 预算有直观认识。
    total_bytes_int8 = total_params * 1
    total_kb_int8 = total_bytes_int8 / 1024.0

    print("\n===== 模型参数统计 =====")
    print(f"总参数量: {total_params:,} params")
    print(f"按 float32 存储约: {total_kb_fp32:.2f} KB")
    print(f"按 int8 量化后理论约: {total_kb_int8:.2f} KB")

    if total_params <= 50000:
        print("满足约束：总参数量 <= 50,000（约 200 KB float32）。")
    else:
        print("警告：参数量超过题目约束，请减小网络规模。")


def compile_model(model, task_type=TASK_TYPE):
    """根据任务类型编译模型。"""
    optimizer = tf.keras.optimizers.Adam(learning_rate=LEARNING_RATE)

    if task_type == "classification":
        model.compile(
            optimizer=optimizer,
            loss="sparse_categorical_crossentropy",
            metrics=["accuracy"],
        )
    else:
        model.compile(
            optimizer=optimizer,
            loss="mse",
            metrics=["mae"],
        )


def generate_demo_data(task_type=TASK_TYPE):
    """生成随机示例数据，仅用于验证训练流程能运行。

    为了更接近真实量化场景，这里故意将输入值控制在较小范围内，
    避免出现极端值，从而让权重更新更平稳。
    """
    rng = np.random.default_rng(seed=42)

    # 输入值限制在相对温和的范围，模拟已归一化/标准化后的传感器序列。
    x_train = rng.normal(loc=0.0, scale=0.5, size=(TRAIN_SAMPLES, TIME_STEPS, FEATURES)).astype(np.float32)
    x_val = rng.normal(loc=0.0, scale=0.5, size=(VAL_SAMPLES, TIME_STEPS, FEATURES)).astype(np.float32)

    if task_type == "classification":
        y_train = rng.integers(0, NUM_CLASSES, size=(TRAIN_SAMPLES,), endpoint=False, dtype=np.int32)
        y_val = rng.integers(0, NUM_CLASSES, size=(VAL_SAMPLES,), endpoint=False, dtype=np.int32)
    else:
        # 回归目标同样保持在较小范围，利于后续量化。
        y_train = rng.normal(loc=0.0, scale=0.25, size=(TRAIN_SAMPLES, REGRESSION_OUTPUTS)).astype(np.float32)
        y_val = rng.normal(loc=0.0, scale=0.25, size=(VAL_SAMPLES, REGRESSION_OUTPUTS)).astype(np.float32)

    return x_train, y_train, x_val, y_val


def main():
    # 固定随机种子，便于在 PyCharm 中多次运行时复现实验。
    np.random.seed(42)
    tf.random.set_seed(42)

    print("TensorFlow version:", tf.__version__)
    print("任务类型:", TASK_TYPE)
    print("训练批大小:", BATCH_SIZE)
    print("部署批大小建议: 1 (STM32 推理)")

    # 1) 构建模型。
    model = build_stm32_friendly_lstm_model(task_type=TASK_TYPE)

    # 2) 打印模型结构，满足题目要求。
    print("\n===== model.summary() =====")
    model.summary()

    # 3) 打印参数量与近似模型大小，满足题目要求。
    print_model_size_info(model)

    # 4) 编译模型。
    compile_model(model, task_type=TASK_TYPE)

    # 5) 生成随机演示数据并训练少量 epoch，仅验证流程可运行。
    x_train, y_train, x_val, y_val = generate_demo_data(task_type=TASK_TYPE)

    history = model.fit(
        x_train,
        y_train,
        validation_data=(x_val, y_val),
        epochs=EPOCHS,
        batch_size=BATCH_SIZE,
        verbose=2,
    )

    # 6) 保存为单一 H5 文件，满足题目命名要求。
    model.save(MODEL_PATH)
    print(f"\n模型已保存到: {MODEL_PATH.resolve()}")

    # 附加输出：显示最后一个 epoch 的关键指标，方便在 PyCharm 控制台查看。
    print("\n===== 最后一个 epoch 指标 =====")
    for key, values in history.history.items():
        print(f"{key}: {values[-1]:.6f}")

    # 推理演示：部署时等价于 batch=1 的输入格式。
    sample_input = np.random.normal(0.0, 0.5, size=(1, TIME_STEPS, FEATURES)).astype(np.float32)
    sample_output = model.predict(sample_input, verbose=0)
    print("\n===== batch=1 推理输出形状 =====")
    print("input shape :", sample_input.shape)
    print("output shape:", sample_output.shape)
    print("sample output:", sample_output)

    # 额外说明：
    # 如果你后续要导入 STM32Cube.AI / X-CUBE-AI，建议在真实数据集上先做：
    # 1) 输入归一化/标准化；
    # 2) 训练后执行 TFLite int8 量化；
    # 3) 使用代表性数据集校准量化范围；
    # 4) 再评估模型 RAM / Flash 占用。


if __name__ == "__main__":
    main()
