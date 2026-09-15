const path = require("path");
const HtmlWebpackPlugin = require("html-webpack-plugin");

module.exports = {
  entry: path.resolve(__dirname, "src", "main.tsx"),
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "assets/[name].[contenthash].js",
    clean: true
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"]
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: "ts-loader",
        exclude: /node_modules/
      },
      {
        test: /\.css$/i,
        use: ["style-loader", "css-loader"]
      },
      {
        test: /\.(png|jpe?g|gif|webp|ico)$/i,
        type: "asset/resource",
        generator: {
          filename: "assets/[name].[contenthash][ext]"
        }
      }
    ]
  },
  plugins: [
    new HtmlWebpackPlugin({
      template: path.resolve(__dirname, "public", "index.html")
    })
  ],
  devServer: {
    static: {
      directory: path.resolve(__dirname, "public")
    },
    historyApiFallback: true,
    client: {
      overlay: true
    }
  }
};
